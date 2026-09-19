namespace Sense.Modules.Leave.Contracts;

/// <summary>One employee's one approved-leave calendar day (a request's [StartDate, EndDate] span expanded into
/// individual dates), for cross-checking against another module's day-level records.</summary>
public sealed record ApprovedLeaveDay(Guid EmployeeId, DateOnly Date);

/// <summary>
/// Leave's read port for Attendance's "izinli günde çalışma" detector (senseik-029, 11.3.6 çip kuralları): lets
/// Attendance flag an <c>AttendanceDay</c> that carries a check-in/out despite an approved leave request covering
/// that date, without depending on Leave's Domain/Infrastructure (Modules_TalkOnlyThroughContracts). Mirrors
/// <see cref="ILeaveDayReader"/>'s per-period/per-employee query shape; unlike it, this expands each request into
/// individual dates because the detector needs day-level, not bucketed, granularity. Implemented by
/// Sense.Modules.Leave.Infrastructure and registered by AddLeaveCoreServices (Api and Worker).
/// </summary>
public interface IApprovedLeaveDateLookup
{
    /// <summary>Approved leave dates intersecting [from, to] for the given employees, one row per employee+date. An
    /// employee with no approved leave in the period does not appear in the result.</summary>
    Task<IReadOnlyList<ApprovedLeaveDay>> GetApprovedLeaveDatesAsync(
        Guid tenantId, DateOnly from, DateOnly to, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);
}
