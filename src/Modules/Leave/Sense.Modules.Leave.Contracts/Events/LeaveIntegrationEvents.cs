using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Leave.Contracts.Events;

/// <summary>
/// Published (via Leave's outbox) when an authorized user cancelled a pending or approved leave request. <c>WasApproved</c>
/// true means the days had been posted as usage and were restored with a Reversal ledger row (Attendance/Payroll undo it).
/// </summary>
public sealed record LeaveCancelled(
    Guid TenantId, Guid LeaveRequestId, Guid EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, decimal Days, bool WasApproved)
    : IntegrationEvent(TenantId);

/// <summary>
/// Published (via Leave's outbox) when a manager/HR added a leave request on an employee's behalf (Source = OnBehalf),
/// so Notifications can inform the employee. <c>LeaveTypeName</c> is denormalized at raise time so consumers don't need
/// a Leave lookup contract just to display it.
/// </summary>
public sealed record LeaveAddedOnBehalf(
    Guid TenantId, Guid LeaveRequestId, Guid EmployeeId, Guid LeaveTypeId, string LeaveTypeName, DateOnly StartDate, DateOnly EndDate, decimal Days)
    : IntegrationEvent(TenantId);
