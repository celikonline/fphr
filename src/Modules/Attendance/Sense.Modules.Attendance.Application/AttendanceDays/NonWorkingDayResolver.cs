using Sense.Modules.Leave.Contracts;
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

    internal NonWorkingDays(HashSet<(Guid, DateOnly)> leave, HashSet<DateOnly> holidays)
    {
        _leave = leave;
        _holidays = holidays;
    }

    /// <summary>True when the tenant's holiday calendar marks the date as an official holiday (leave days excluded).</summary>
    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    /// <summary>True when the employee has approved leave on the date (official holidays excluded).</summary>
    public bool IsOnLeave(Guid employeeId, DateOnly date) => _leave.Contains((employeeId, date));

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
        if (leaveDates is not null && employeeIds.Count > 0)
        {
            var approved = await leaveDates.GetApprovedLeaveDatesAsync(tenantId, from, to, employeeIds, cancellationToken).ConfigureAwait(false);
            leave.UnionWith(approved.Select(l => (l.EmployeeId, l.Date)));
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

        return new NonWorkingDays(leave, holidayDates);
    }
}
