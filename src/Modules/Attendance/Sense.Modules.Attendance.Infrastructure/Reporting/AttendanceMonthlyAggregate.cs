using Sense.Modules.Attendance.Domain.AttendanceDays;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// One employee's AttendanceDay rows for a period, reduced to the numbers both report providers show (senseik-029):
/// recorded/worked day counts, worked/overtime/night hours and anomaly counts. Built purely from already-persisted
/// AttendanceDay fields - no schedule lookups - so both <c>attendance.monthly.summary</c> and
/// <c>attendance.monthly.closing</c> compute identical totals from the same rows.
/// </summary>
internal sealed record AttendanceMonthlyAggregate(
    int RecordedDays,
    int WorkedDays,
    decimal WorkedHours,
    decimal OvertimeHours,
    decimal NightHours,
    int LateArrivalCount,
    int EarlyLeaveCount,
    int MissingDayCount,
    int WorkedWhileOnLeaveCount,
    int UnresolvedCount)
{
    private const int MinutesPerHour = 60;
    private static readonly AttendanceAnomaly MissingFlags = AttendanceAnomaly.NoData | AttendanceAnomaly.MissingCheckIn | AttendanceAnomaly.MissingCheckOut;

    public static AttendanceMonthlyAggregate From(IReadOnlyCollection<AttendanceDay> days)
    {
        var workedMinutes = 0;
        var overtimeMinutes = 0;
        var nightMinutes = 0;
        var workedDays = 0;
        var lateArrival = 0;
        var earlyLeave = 0;
        var missing = 0;
        var workedWhileOnLeave = 0;
        var unresolved = 0;

        foreach (var day in days)
        {
            var minutes = day.OverrideWorkedMinutes ?? day.WorkedMinutes;
            workedMinutes += minutes;
            overtimeMinutes += day.OvertimeMinutes;
            nightMinutes += day.NightMinutes;
            if (minutes > 0)
            {
                workedDays++;
            }

            if (day.Anomalies.HasFlag(AttendanceAnomaly.LateArrival))
            {
                lateArrival++;
            }

            if (day.Anomalies.HasFlag(AttendanceAnomaly.EarlyLeave))
            {
                earlyLeave++;
            }

            if ((day.Anomalies & MissingFlags) != 0)
            {
                missing++;
            }

            if (day.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave))
            {
                workedWhileOnLeave++;
            }

            if (day.ResolutionState == AttendanceResolutionState.Unresolved)
            {
                unresolved++;
            }
        }

        return new AttendanceMonthlyAggregate(
            days.Count, workedDays, MinutesToHours(workedMinutes), MinutesToHours(overtimeMinutes), MinutesToHours(nightMinutes),
            lateArrival, earlyLeave, missing, workedWhileOnLeave, unresolved);
    }

    private static decimal MinutesToHours(int minutes) => Math.Round(minutes / (decimal)MinutesPerHour, 2);
}
