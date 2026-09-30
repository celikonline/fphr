using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Payroll.Application.Payslips;

/// <summary>
/// Workflow approved a "PayslipObjection" request: the objection is accepted and <see cref="PayslipObjectionAccepted"/>
/// is enqueued so the payroll role (<see cref="PayrollRoleCodes.PayrollSpecialist"/>) is told to correct the payslip.
/// Runs in Sense.Worker; a redelivered or stale event (request id no longer the payslip's pending objection) is ignored.
/// </summary>
public sealed class PayslipObjectionApprovalCompletedHandler(
    IPayslipRepository payslips, IPayrollPeriodRepository periods, IIntegrationEventOutbox outbox, IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalCompleted>
{
    public async Task Handle(ApprovalCompleted integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.PayslipObjection
            || await payslips.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } payslip
            || payslip.AcceptObjection(integrationEvent.ApprovalRequestId, clock.GetUtcNow()).IsFailure)
        {
            return;
        }

        var period = await periods.FindByIdAsync(payslip.PayrollPeriodId, cancellationToken);
        outbox.Enqueue(new PayslipObjectionAccepted(
            payslip.TenantId, payslip.Id, payslip.EmployeeId, payslip.PayrollPeriodId, period?.Year ?? 0, period?.Month ?? 0,
            integrationEvent.ApprovalRequestId, PayrollRoleCodes.PayrollSpecialist));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Workflow rejected a "PayslipObjection" request: the payslip stands and can be acknowledged (Workflow tells the requester).</summary>
public sealed class PayslipObjectionApprovalRejectedHandler(IPayslipRepository payslips, IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : IIntegrationEventHandler<ApprovalRejected>
{
    public async Task Handle(ApprovalRejected integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.RequestType != ApprovalRequestTypes.PayslipObjection
            || await payslips.FindByIdAsync(integrationEvent.SubjectId, cancellationToken) is not { } payslip
            || payslip.RejectObjection(integrationEvent.ApprovalRequestId, clock.GetUtcNow()).IsFailure)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
