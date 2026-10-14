namespace Sense.Modules.Leave.Contracts;

/// <summary>One employee's one approved-leave calendar day (a request's [StartDate, EndDate] span expanded into
/// individual dates), for cross-checking against another module's day-level records.</summary>
public sealed record ApprovedLeaveDay(Guid EmployeeId, DateOnly Date, bool IsPaid = true, decimal DayFraction = 1m, bool IsSick = false, DateTimeOffset? ChangedAt = null, TimeOnly? StartsAt = null, TimeOnly? EndsAt = null);

/// <summary>One approved-leave calendar day of a single employee with the leave type's name. <paramref name="LeaveTypeName"/>
/// is null when the type is masked (sensitive/KVKK, hidden from the calendar, or no longer known) - same rule as Leave's
/// own calendar; the day is still an approved leave day. Seeded types carry their resource key (e.g. "leave.type.annual").</summary>
public sealed record ApprovedLeaveDayDetail(DateOnly Date, string? LeaveTypeName);

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

    /// <summary>One employee's approved leave dates intersecting [from, to] with the (masked) type name, one row per date,
    /// oldest first - for Attendance's self-service "Puantajım" (mobil yeniden tasarım). When two approved requests cover
    /// the same date the first unmasked name wins.</summary>
    Task<IReadOnlyList<ApprovedLeaveDayDetail>> GetApprovedLeaveDaysWithTypeAsync(
        Guid tenantId, Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
