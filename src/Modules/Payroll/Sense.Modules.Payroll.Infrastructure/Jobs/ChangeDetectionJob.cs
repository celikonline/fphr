using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Application.Ports;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Modules.Payroll.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Payroll.Infrastructure.Jobs;

/// <summary>
/// ChangeDetectionRun ("5 dk mutabakat", 11.3.07-bordro.md "Değişti mekanizması" (b), P3 "event kaçırma sigortası"):
/// for every Open period, compares each payslip's stored InputHash against a hash of the employee's CURRENT salary +
/// payroll profile (same <see cref="PayrollInputHashing"/> the calculation itself uses); a mismatch means something
/// changed without going through one of the dirty-flag event handlers (PayslipDirtyFlagHandlers) or was missed, so it
/// gets flagged directly with <see cref="PayslipDirtyFlagReasons.HashMismatch"/>. Deliberately narrow per the P3 task
/// scope for non-hourly wages; hourly paid-time and approval inputs are also compared, using the same employment window
/// as the calculation. Mirrors LeaveBalanceRecalculationJob's per-tenant scope/transaction pattern.
/// </summary>
public sealed partial class ChangeDetectionJob(IServiceScopeFactory scopes, ILogger<ChangeDetectionJob> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        List<Guid> tenantIds;
        using (var scope = scopes.CreateScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<PayrollDbContext>().Periods
                .IgnoreQueryFilters([ModuleDbContext.TenantFilter])
                .Where(p => p.Status == PayrollPeriodStatus.Open)
                .Select(p => p.TenantId).Distinct().ToListAsync(cancellationToken);
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                total += await ScanTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenantId);
            }
        }

        LogCompleted(logger, total, tenantIds.Count);
        return total;
    }

    private async Task<int> ScanTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().BeginScope(tenantId);
        var periods = scope.ServiceProvider.GetRequiredService<IPayrollPeriodRepository>();
        var payslips = scope.ServiceProvider.GetRequiredService<IPayslipRepository>();
        var profiles = scope.ServiceProvider.GetRequiredService<IEmployeePayrollProfileRepository>();
        var dirtyFlags = scope.ServiceProvider.GetRequiredService<IPayslipDirtyFlagRepository>();
        var runs = scope.ServiceProvider.GetRequiredService<IChangeDetectionRunRepository>();
        var salaryLookup = scope.ServiceProvider.GetRequiredService<IEmployeeSalaryLookup>();
        var costCenterReader = scope.ServiceProvider.GetRequiredService<IEmployeeCostCenterReader>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IPayrollUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var openPeriods = await periods.ListOpenAsync(cancellationToken);
        var dirtyTotal = 0;
        foreach (var period in openPeriods)
        {
            var startedAt = clock.GetUtcNow().UtcDateTime;
            var run = ChangeDetectionRun.Start(tenantId, period.Id, ChangeDetectionTrigger.Scheduled, startedAt);
            runs.Add(run);

            var periodStart = new DateOnly(period.Year, period.Month, 1);
            var periodEnd = new DateOnly(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month));
            var payslipsInPeriod = await payslips.ListByPeriodAsync(period.Id, cancellationToken);
            var costCenterMapping = await costCenterReader.GetEmployeeCostCenterMappingAsync(
                tenantId, payslipsInPeriod.Select(p => p.EmployeeId).ToList(), cancellationToken);
            var dirtyCount = 0;
            foreach (var payslip in payslipsInPeriod)
            {
                var profile = await profiles.FindCurrentAsync(payslip.EmployeeId, periodStart, cancellationToken);
                var employment = await salaryLookup.GetEmploymentDatesAsync(tenantId, payslip.EmployeeId, cancellationToken);
                var salary = await salaryLookup.GetSalaryAsOfAsync(tenantId, payslip.EmployeeId,
                    EmploymentProration.EffectiveEnd(periodEnd, employment?.TerminationDate), cancellationToken);
                if (profile is null || salary is null)
                {
                    continue; // nothing live to compare against - a removed profile/salary is not a "hash mismatch"
                }

                Guid? costCenterId = costCenterMapping.TryGetValue(payslip.EmployeeId, out var assignedCostCenterId)
                    ? assignedCostCenterId
                    : null;
                WorkedTimeSummary? hourlyAttendance = null;
                if (profile.WageBasis == WageBasis.Hourly)
                {
                    var workedTimeReader = scope.ServiceProvider.GetRequiredService<IWorkedTimeReader>();
                    var partial = EmploymentProration.PaidDays(periodStart, periodEnd, employment?.HireDate, employment?.TerminationDate) < 30;
                    hourlyAttendance = await workedTimeReader.GetAsync(tenantId, payslip.EmployeeId, period.Year, period.Month,
                        partial ? EmploymentProration.EffectiveStart(periodStart, employment?.HireDate) : null,
                        partial ? EmploymentProration.EffectiveEnd(periodEnd, employment?.TerminationDate) : null, cancellationToken);
                }

                var currentHash = PayrollInputHashing.Hash(PayrollInputHashing.BuildSnapshot(profile, salary, costCenterId, hourlyAttendance));
                if (currentHash == payslip.InputHash)
                {
                    continue;
                }

                if (await dirtyFlags.FindActiveAsync(period.Id, payslip.EmployeeId, cancellationToken) is null)
                {
                    dirtyFlags.Add(PayslipDirtyFlag.Create(tenantId, period.Id, payslip.EmployeeId, PayslipDirtyFlagReasons.HashMismatch, sourceEventId: null, clock.GetUtcNow().UtcDateTime));
                    dirtyCount++;
                }
            }

            run.Finish(payslipsInPeriod.Count, dirtyCount, clock.GetUtcNow().UtcDateTime);
            dirtyTotal += dirtyCount;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dirtyTotal;
    }

    [LoggerMessage(EventId = 7401, Level = LogLevel.Error, Message = "Payroll change detection failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(EventId = 7402, Level = LogLevel.Information, Message = "Payroll change detection flagged {Dirty} payslip(s) across {Tenants} tenant(s)")]
    private static partial void LogCompleted(ILogger logger, int dirty, int tenants);
}

/// <summary>
/// Runs <see cref="ChangeDetectionJob"/> every <see cref="IntervalMinutes"/> (Sense.Worker). The interval is a
/// hardcoded default: the spec's <c>Payroll.ChangeDetection.IntervalMinutes</c> tenant setting does not exist yet
/// (TenantSettings key catalog is out of this slice's scope, P3 task decision) - deferred, see faz3-durum-takibi.md.
/// </summary>
public sealed partial class ChangeDetectionHostedService(ChangeDetectionJob job, TimeProvider clock, ILogger<ChangeDetectionHostedService> logger) : BackgroundService
{
    /// <summary>TODO(Payroll.ChangeDetection.IntervalMinutes): hardcoded 5-minute default until the tenant setting exists.</summary>
    public static readonly TimeSpan IntervalMinutes = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, clock, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await job.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRunFailed(logger, ex);
            }

            await Task.Delay(IntervalMinutes, clock, stoppingToken);
        }
    }

    [LoggerMessage(EventId = 7403, Level = LogLevel.Error, Message = "Payroll change detection run failed")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
