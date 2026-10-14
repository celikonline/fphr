using Sense.Modules.Leave.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Organization.Contracts;

namespace Sense.Modules.Attendance.Application.AttendanceDays;

/// <summary>
/// The days on which a scheduled working day does not actually have to be worked: the tenant's official holidays
/// (Organization.Contracts.IHolidayCalendarLookup) and each employee's approved leave (Leave.Contracts.IApprovedLeaveDateLookup).
/// Attendance's own work schedule knows neither, so an empty holiday/leave day would otherwise read as "veri yok / eksik gün".
/// </summary>
public sealed class NonWorkingDays
{
    public static readonly NonWorkingDays None = new(new HashSet<(Guid, DateOnly)>(), new HashSet<DateOnly>());

    private readonly HashSet<(Guid EmployeeId, DateOnly Date)> _leave;
    private readonly HashSet<DateOnly> _holidays;
    private readonly Dictionary<(Guid, DateOnly), ApprovedLeaveDay[]> _details;

    internal NonWorkingDays(HashSet<(Guid, DateOnly)> leave, HashSet<DateOnly> holidays, IReadOnlyList<ApprovedLeaveDay>? details = null)
    {
        _leave = leave;
        _holidays = holidays;
        _details = (details ?? []).GroupBy(d => (d.EmployeeId, d.Date)).ToDictionary(g => g.Key, g => g.ToArray());
    }

    /// <summary>True when the tenant's holiday calendar marks the date as an official holiday (leave days excluded).</summary>
    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    /// <summary>True when the employee has approved leave on the date (official holidays excluded).</summary>
    public bool IsOnLeave(Guid employeeId, DateOnly date) => _leave.Contains((employeeId, date));

    public bool HasLeave(Guid employeeId) => _details.Keys.Any(k => k.Item1 == employeeId);

    public decimal LeaveFraction(Guid employeeId, DateOnly date, bool? paid = null, bool? sick = null) =>
        Math.Min(1m, _details.GetValueOrDefault((employeeId, date), [])
            .Where(d => (paid is null || d.IsPaid == paid) && (sick is null || d.IsSick == sick)).Sum(d => d.DayFraction));

    public DateTimeOffset? LatestLeaveChange(Guid employeeId) => _details.Values.SelectMany(d => d)
        .Where(d => d.EmployeeId == employeeId).Select(d => d.ChangedAt).Max();

    public bool IsApprovalCurrent(Guid employeeId, DateTime approvedAt, IEnumerable<AttendanceDay> days) =>
        (LatestLeaveChange(employeeId) is not { } changedAt || changedAt <= approvedAt)
        && days.All(d => (d.ModifiedDate ?? d.CreatedAt) <= approvedAt
            && (d.Anomalies == AttendanceAnomaly.None || d.ResolutionState != AttendanceResolutionState.Unresolved));

    public bool Contains(Guid employeeId, DateOnly date) => _holidays.Contains(date) || _leave.Contains((employeeId, date));
}

/// <summary>
/// Both lookups are optional on purpose: Attendance's core services are also registered in hosts/fixtures that do not carry the
/// Organization (holiday calendar) or Leave module (see <see cref="LeaveOverlapChecker"/>'s doc); there the result simply degrades
/// to "no holiday/leave info" - the behaviour before this resolver existed - instead of failing DI validation.
/// </summary>
public sealed class NonWorkingDayResolver(IApprovedLeaveDateLookup? leaveDates = null, IHolidayCalendarLookup? holidays = null)
{
    public async Task<NonWorkingDays> ResolveAsync(
        Guid tenantId, IReadOnlyCollection<Guid> employeeIds, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var leave = new HashSet<(Guid, DateOnly)>();
        IReadOnlyList<ApprovedLeaveDay> approved = [];
        if (leaveDates is not null && employeeIds.Count > 0)
        {
            approved = await leaveDates.GetApprovedLeaveDatesAsync(tenantId, from, to, employeeIds, cancellationToken).ConfigureAwait(false);
            leave.UnionWith(approved.GroupBy(l => (l.EmployeeId, l.Date)).Where(g => g.Sum(l => l.DayFraction) >= 1m).Select(g => g.Key));
        }

        var holidayDates = new HashSet<DateOnly>();
        if (holidays is not null)
        {
            var calendar = await holidays.ResolveCalendarAsync(tenantId, null, null, cancellationToken).ConfigureAwait(false);
            if (calendar is not null)
            {
                var occurrences = await holidays.GetHolidaysAsync(tenantId, calendar.Id, from, to, cancellationToken).ConfigureAwait(false);
                holidayDates.UnionWith(occurrences.Select(h => h.Date));
            }
        }

        return new NonWorkingDays(leave, holidayDates, approved);
    }
}
