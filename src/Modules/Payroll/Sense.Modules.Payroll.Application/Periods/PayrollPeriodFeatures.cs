using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

public sealed record PayrollPeriodDto(Guid Id, int Year, int Month, PayrollPeriodStatus Status);

/// <summary>Opens a new tenant-month payroll period ("Dönem aç", 11.3.07 akış §2). Reuses payroll.period.recalculate
/// (no separate "open" permission key - opening is the first step of the same recalculation workflow).</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record OpenPayrollPeriodCommand(int Year, int Month) : ICommand<Guid>;

public sealed class OpenPayrollPeriodValidator : AbstractValidator<OpenPayrollPeriodCommand>
{
    public OpenPayrollPeriodValidator()
    {
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).GreaterThan(2000);
    }
}

public sealed class OpenPayrollPeriodHandler(ITenantContext tenant, IPayrollPeriodRepository periods) : ICommandHandler<OpenPayrollPeriodCommand, Guid>
{
    public async Task<Result<Guid>> Handle(OpenPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        if (await periods.FindByYearMonthAsync(command.Year, command.Month, cancellationToken) is not null)
        {
            return Error.Conflict(PayrollDomainErrors.PeriodAlreadyOpen);
        }

        var created = PayrollPeriod.Create(tenant.TenantId, command.Year, command.Month);
        if (created.IsFailure)
        {
            return created.Error;
        }

        periods.Add(created.Value);
        return created.Value.Id;
    }
}

/// <summary>
/// Recalculates every active employee's payslip in an Open period (11.3.07 akış §2 "hesapla" + §4 "Tümünü yeniden
/// hesapla" - the same operation, callable again after the initial run: see PayrollPeriodCalculationService doc).
/// Rejected on a non-Open period (Approved/Paid/Locked) - "Approved+ dönemde bayrak kalır, yeniden hesap yerine
/// Supplementary önerilir" (Supplementary runs are out of scope, P5+).
/// </summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record RecalculateAllPayslipsCommand(Guid PeriodId) : ICommand<PayrollPeriodRunSummary>;

public sealed class RecalculateAllPayslipsHandler(
    ITenantContext tenant, IPayrollPeriodRepository periods, IEmployeeDirectory employeeDirectory, PayrollPeriodCalculationService calculation)
    : ICommandHandler<RecalculateAllPayslipsCommand, PayrollPeriodRunSummary>
{
    public async Task<Result<PayrollPeriodRunSummary>> Handle(RecalculateAllPayslipsCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotOpen);
        }

        var employeeIds = await employeeDirectory.ListActiveEmployeeIdsAsync(tenant.TenantId, cancellationToken);
        return await calculation.RecalculateAllAsync(period, employeeIds, cancellationToken);
    }
}

/// <summary>Recalculates only employees with an active dirty flag in this period ("Değişenleri yeniden hesapla", akış
/// §3). Same Open-period restriction as <see cref="RecalculateAllPayslipsCommand"/>.</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record RecalculateDirtyPayslipsCommand(Guid PeriodId) : ICommand<PayrollPeriodRunSummary>;

public sealed class RecalculateDirtyPayslipsHandler(IPayrollPeriodRepository periods, PayrollPeriodCalculationService calculation)
    : ICommandHandler<RecalculateDirtyPayslipsCommand, PayrollPeriodRunSummary>
{
    public async Task<Result<PayrollPeriodRunSummary>> Handle(RecalculateDirtyPayslipsCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if (period.Status != PayrollPeriodStatus.Open)
        {
            return Error.Validation(PayrollDomainErrors.PeriodNotOpen);
        }

        return await calculation.RecalculateDirtyAsync(period, cancellationToken);
    }
}

/// <summary>One active "Değişti" flag, for the future "Değişenler" filter screen (P8).</summary>
public sealed record PayslipDirtyFlagDto(Guid Id, Guid EmployeeId, IReadOnlyList<string> Reasons, DateTime DetectedAt);

/// <summary>Every active dirty flag in a period (GET .../dirty-flags).</summary>
[RequiresPermission("payroll.period.recalculate")]
public sealed record ListDirtyFlagsQuery(Guid PeriodId) : IQuery<IReadOnlyList<PayslipDirtyFlagDto>>;

public sealed class ListDirtyFlagsHandler(IPayrollPeriodRepository periods, IPayslipDirtyFlagRepository dirtyFlags)
    : IQueryHandler<ListDirtyFlagsQuery, IReadOnlyList<PayslipDirtyFlagDto>>
{
    public async Task<Result<IReadOnlyList<PayslipDirtyFlagDto>>> Handle(ListDirtyFlagsQuery query, CancellationToken cancellationToken)
    {
        if (await periods.FindByIdAsync(query.PeriodId, cancellationToken) is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var flags = await dirtyFlags.ListActiveByPeriodAsync(query.PeriodId, cancellationToken);
        return flags.Select(f => new PayslipDirtyFlagDto(f.Id, f.EmployeeId, f.Reasons, f.DetectedAt)).ToList();
    }
}
