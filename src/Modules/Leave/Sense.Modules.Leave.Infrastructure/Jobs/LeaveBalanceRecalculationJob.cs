using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Leave.Infrastructure.Jobs;

/// <summary>
/// RecalculateBalances job (11.3.5, roadmap #15): regenerates Accrual/CarryOver rows of every employee leave profile up to
/// today, tenant by tenant (each tenant in its own tenant scope, DI scope and transaction). Idempotent — generated rows are
/// replaced, Usage/ExtraGrant/Adjustment/OpeningBalance rows are kept — so running it daily moves accruals and period
/// roll-overs forward without an event.
/// </summary>
public sealed partial class LeaveBalanceRecalculationJob(IServiceScopeFactory scopes, ILogger<LeaveBalanceRecalculationJob> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        List<Guid> tenantIds;
        using (var scope = scopes.CreateScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<LeaveDbContext>().Profiles
                .IgnoreQueryFilters([ModuleDbContext.TenantFilter])
                .Select(p => p.TenantId).Distinct().ToListAsync(cancellationToken);
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                total += await RecalculateTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenantId);
            }
        }

        LogCompleted(logger, total, tenantIds.Count);
        return total;
    }

    private async Task<int> RecalculateTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().BeginScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var ruleSets = scope.ServiceProvider.GetRequiredService<ILeaveRuleSetRepository>();
        var balances = scope.ServiceProvider.GetRequiredService<LeaveBalanceService>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ILeaveUnitOfWork>();

        var profiles = await db.Profiles.ToListAsync(cancellationToken);
        var sets = new Dictionary<Guid, LeaveRuleSet?>();
        var count = 0;
        foreach (var profile in profiles)
        {
            if (!sets.TryGetValue(profile.RuleSetId, out var set))
            {
                var loaded = await ruleSets.GetByIdAsync(profile.RuleSetId, cancellationToken);
                set = loaded.IsSuccess ? loaded.Value : null;
                sets[profile.RuleSetId] = set;
            }

            if (set is null)
            {
                continue;
            }

            await balances.RecalculateAsync(profile, set, cancellationToken);
            count++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return count;
    }

    [LoggerMessage(EventId = 7301, Level = LogLevel.Error, Message = "Leave balance recalculation failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information, Message = "Leave balances recalculated for {Profiles} profiles in {Tenants} tenants")]
    private static partial void LogCompleted(ILogger logger, int profiles, int tenants);
}

/// <summary>Runs <see cref="LeaveBalanceRecalculationJob"/> shortly after start and then every day at 01:00 UTC (Sense.Worker).</summary>
public sealed partial class LeaveBalanceRecalculationHostedService(
    LeaveBalanceRecalculationJob job, TimeProvider clock, ILogger<LeaveBalanceRecalculationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    public static TimeSpan DelayUntilNextRun(DateTimeOffset nowUtc)
    {
        var next = nowUtc.UtcDateTime.Date + RunAtUtc;
        if (next <= nowUtc.UtcDateTime)
        {
            next = next.AddDays(1);
        }

        return next - nowUtc.UtcDateTime;
    }

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

            await Task.Delay(DelayUntilNextRun(clock.GetUtcNow()), clock, stoppingToken);
        }
    }

    [LoggerMessage(EventId = 7303, Level = LogLevel.Error, Message = "Leave balance recalculation run failed")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
