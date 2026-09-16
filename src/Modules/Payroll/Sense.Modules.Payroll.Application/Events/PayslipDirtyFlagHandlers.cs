using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Leave.Contracts.Events;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Payroll.Application.Events;

/// <summary>
/// Cross-module "Değişti" consumers (11.3.07-bordro.md "Değişti mekanizması", P3): each resolves which Open
/// <see cref="Domain.Periods.PayrollPeriod"/>(s) the change affects (by date) and writes/merges a
/// <see cref="PayslipDirtyFlag"/> via <see cref="PayslipDirtyFlagService"/>. Only wired for events that already exist
/// today (checked against Employee.Contracts.Events and Leave.Contracts.Events at P3 time):
/// - Employee.Contracts.Events.SalaryChanged, Employee.Contracts.Events.EmployeeProfileChanged (Bank section).
/// - Leave.Contracts.Events.LeaveCancelled (the only Leave.Leave*/Overtime* event that exists; LeaveApproved and
///   OvertimeConvertedToPay do not exist in Leave.Contracts yet - faz3-durum-takibi.md P3 entry documents this as
///   deferred, to be wired when Leave adds them, rather than fabricated here).
/// Runs outside the command pipeline (OutboxProcessor → IEventBus → these handlers, in Sense.Worker), so each Handle
/// saves explicitly, mirroring EmployeeHiredHandler (P2).
/// </summary>
public sealed class SalaryChangedDirtyFlagHandler(PayslipDirtyFlagService flagging, IPayrollUnitOfWork unitOfWork) : IIntegrationEventHandler<SalaryChanged>
{
    public async Task Handle(SalaryChanged integrationEvent, CancellationToken cancellationToken)
    {
        await flagging.FlagAsync(
            integrationEvent.TenantId, integrationEvent.EmployeeId, integrationEvent.EffectiveFrom,
            PayslipDirtyFlagReasons.SalaryChanged, integrationEvent.EventId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Only <see cref="EmployeeProfileSections.Bank"/> is payroll-relevant today (payment method/IBAN feed
/// <c>EmployeePayrollProfile.BankAccountId</c>); other sections (custom fields, self-service) do not affect a
/// calculation and are ignored. The event carries no date, so every currently Open period is flagged (the change is
/// effective "now").</summary>
public sealed class EmployeeProfileChangedDirtyFlagHandler(PayslipDirtyFlagService flagging, IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<EmployeeProfileChanged>
{
    public async Task Handle(EmployeeProfileChanged integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Section != EmployeeProfileSections.Bank)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await flagging.FlagAsync(
            integrationEvent.TenantId, integrationEvent.EmployeeId, today,
            PayslipDirtyFlagReasons.BankChanged, integrationEvent.EventId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A cancelled (previously approved) leave changes worked/payable days for its date range - flags every Open
/// period from <see cref="LeaveCancelled.StartDate"/> onward. Pending (never-approved) cancellations never touched a
/// payslip, but flagging anyway is harmless (RecalculateDirty is idempotent).</summary>
public sealed class LeaveCancelledDirtyFlagHandler(PayslipDirtyFlagService flagging, IPayrollUnitOfWork unitOfWork) : IIntegrationEventHandler<LeaveCancelled>
{
    public async Task Handle(LeaveCancelled integrationEvent, CancellationToken cancellationToken)
    {
        await flagging.FlagAsync(
            integrationEvent.TenantId, integrationEvent.EmployeeId, integrationEvent.StartDate,
            PayslipDirtyFlagReasons.LeaveChanged, integrationEvent.EventId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
