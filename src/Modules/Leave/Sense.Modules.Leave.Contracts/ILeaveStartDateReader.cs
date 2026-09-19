namespace Sense.Modules.Leave.Contracts;

/// <summary>One employee's approved leave start date.</summary>
public sealed record LeaveStartDate(Guid EmployeeId, DateOnly StartDate);

/// <summary>
/// Leave's read port for Announcements' company calendar (senseik-027, roadmap #71/P-19): lets the calendar reflect
/// approved leave starts without depending on Leave's Domain or Infrastructure, mirroring ILeaveDayReader's role for
/// Attendance. Sensitive leave types (<c>LeaveType.IsSensitive</c>, KVKK health data) are excluded entirely - not
/// masked - because the company calendar is a broad, everyone-sees-it surface, unlike Leave's own calendar which
/// masks the type name but still shows "on leave" for a sensitive type to the visible audience.
/// Implemented by Sense.Modules.Leave.Infrastructure and registered by AddLeaveCoreServices (Api and Worker).
/// </summary>
public interface ILeaveStartDateReader
{
    /// <summary>Approved leave requests whose StartDate falls in [from, to], non-sensitive types only.</summary>
    Task<IReadOnlyList<LeaveStartDate>> GetApprovedLeaveStartsAsync(
        Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
