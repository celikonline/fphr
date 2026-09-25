namespace Sense.Modules.Attendance.Contracts;

/// <summary>
/// The employee's effective work schedule day on one date (11.3.6 "SenseİK tasarım kararı"). <see cref="HasSchedule"/> is false when
/// neither an assignment nor a tenant default schedule exists (zaman-devam §5.1 "plan yok" - distinct from a schedule's off day).
/// </summary>
public sealed record EmployeeScheduleDay(
    bool IsWorkingDay, TimeOnly? Start, TimeOnly? End, TimeOnly? BreakStart, TimeOnly? BreakEnd, bool IncludeSaturdayWhenFullWeek,
    bool HasSchedule = true);

/// <summary>
/// Cross-module read port other modules use instead of guessing an employee's working days (roadmap #23). Leave's
/// day counter and FM schedule-overlap math consume this; before Attendance existed they used a Monday–Friday
/// placeholder (Sense.Modules.Leave.Infrastructure.MondayToFridayWorkScheduleReader).
/// </summary>
public interface IWorkScheduleReadService
{
    /// <summary>The schedule day in effect on <paramref name="date"/> (falls back to the tenant's default schedule if the employee has no assignment yet).</summary>
    Task<EmployeeScheduleDay> GetForDateAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Working days of the employee's assigned schedule(s) across [from, to] (a schedule change mid-range is honored at its EffectiveFrom).</summary>
    Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<bool> IsWorkingDayAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);
}
