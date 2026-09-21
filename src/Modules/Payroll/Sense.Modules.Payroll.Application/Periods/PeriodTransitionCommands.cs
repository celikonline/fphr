using FluentValidation;
using Sense.Modules.Payroll.Application.Closing;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>
/// Approves a calculated period (Open → Approved). Refused while any payslip still carries an active "Değişti" flag (an
/// approval must cover figures recalculated after the last change) and while a blocking closing check of the preparation
/// or calculation stage is failed or pending; a waiver with a reason clears a check.
/// </summary>
[RequiresPermission("payroll.period.approve")]
public sealed record ApprovePayrollPeriodCommand(Guid PeriodId) : ICommand;

public sealed class ApprovePayrollPeriodHandler(IPayrollPeriodRepository periods, IPayslipDirtyFlagRepository dirtyFlags, IClosingChecklistGate checklist)
    : ICommandHandler<ApprovePayrollPeriodCommand>
{
    public async Task<Result> Handle(ApprovePayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        if ((await dirtyFlags.ListActiveByPeriodAsync(period.Id, cancellationToken)).Count > 0)
        {
            return Error.Validation(PayrollDomainErrors.PeriodHasDirtyPayslips);
        }

        if (await checklist.HasOpenBlockersBeforeApprovalAsync(period, cancellationToken))
        {
            return Error.Validation(PayrollDomainErrors.PeriodHasOpenBlockers);
        }

        return period.Approve();
    }
}

/// <summary>Marks an approved period as paid (Approved → Paid) once the salaries went out.</summary>
[RequiresPermission("payroll.period.approve")]
public sealed record MarkPayrollPeriodPaidCommand(Guid PeriodId) : ICommand;

public sealed class MarkPayrollPeriodPaidHandler(IPayrollPeriodRepository periods) : ICommandHandler<MarkPayrollPeriodPaidCommand>
{
    public async Task<Result> Handle(MarkPayrollPeriodPaidCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        return period is null ? Error.NotFound(PayrollDomainErrors.PeriodNotFound) : period.MarkPaid();
    }
}

/// <summary>Locks a paid period (Paid → Locked); a locked period no longer changes.</summary>
[RequiresPermission("payroll.period.lock")]
public sealed record LockPayrollPeriodCommand(Guid PeriodId) : ICommand;

public sealed class LockPayrollPeriodHandler(IPayrollPeriodRepository periods) : ICommandHandler<LockPayrollPeriodCommand>
{
    public async Task<Result> Handle(LockPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        return period is null ? Error.NotFound(PayrollDomainErrors.PeriodNotFound) : period.Lock();
    }
}

/// <summary>Sends an approved, paid or locked period back to Open. Needs its own permission and a written reason, which is kept with the audit trail.</summary>
[RequiresPermission("payroll.period.reopen")]
public sealed record ReopenPayrollPeriodCommand(Guid PeriodId, string Reason) : ICommand;

public sealed class ReopenPayrollPeriodValidator : AbstractValidator<ReopenPayrollPeriodCommand>
{
    public ReopenPayrollPeriodValidator()
    {
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PayrollLimits.ReopenReasonMaxLength);
    }
}

public sealed class ReopenPayrollPeriodHandler(IPayrollPeriodRepository periods, TimeProvider clock) : ICommandHandler<ReopenPayrollPeriodCommand>
{
    public async Task<Result> Handle(ReopenPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(command.PeriodId, cancellationToken);
        return period is null ? Error.NotFound(PayrollDomainErrors.PeriodNotFound) : period.Reopen(command.Reason, clock.GetUtcNow());
    }
}
