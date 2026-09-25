using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Payroll.Contracts;

/// <summary>
/// Role codes Payroll addresses notifications to. Mirrors Identity's SystemRoleCodes (Payroll cannot reference
/// Identity.Domain); consumers resolve members through Identity.Contracts.IRoleAssignmentLookup.
/// </summary>
public static class PayrollRoleCodes
{
    /// <summary>"Bordro Uzmanı" system role - owns payslip corrections.</summary>
    public const string PayrollSpecialist = "PayrollSpecialist";
}

/// <summary>One published payslip the employee has not acknowledged yet.</summary>
public sealed record UnacknowledgedPayslip(Guid PayslipId, Guid EmployeeId, Guid PeriodId, int Year, int Month);

/// <summary>
/// Raised (Payroll outbox) by the daily reminder job or the manual "Hatırlat" trigger (E2): each listed employee should
/// get <c>payroll.payslip_acknowledgement_reminder</c>. Amounts are never carried.
/// </summary>
public sealed record PayslipAcknowledgementReminderDue(Guid TenantId, IReadOnlyList<UnacknowledgedPayslip> Payslips)
    : IntegrationEvent(TenantId);

/// <summary>
/// Raised (Payroll outbox) when Workflow approves a "PayslipObjection" request, i.e. the employee's objection is
/// accepted and the payslip has to be corrected. <paramref name="NotifyRoleCode"/> is the role whose members must be
/// told (<see cref="PayrollRoleCodes.PayrollSpecialist"/>); the requester is already told by Workflow's request.approved.
/// </summary>
public sealed record PayslipObjectionAccepted(
    Guid TenantId, Guid PayslipId, Guid EmployeeId, Guid PeriodId, int Year, int Month, Guid ApprovalRequestId, string NotifyRoleCode)
    : IntegrationEvent(TenantId);
