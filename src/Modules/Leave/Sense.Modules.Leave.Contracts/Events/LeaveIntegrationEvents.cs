using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Leave.Contracts.Events;

/// <summary>
/// Published (via Leave's outbox) when an authorized user cancelled a pending or approved leave request. <c>WasApproved</c>
/// true means the days had been posted as usage and were restored with a Reversal ledger row (Attendance/Payroll undo it).
/// </summary>
public sealed record LeaveCancelled(
    Guid TenantId, Guid LeaveRequestId, Guid EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, decimal Days, bool WasApproved)
    : IntegrationEvent(TenantId);
