using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain.AttendanceDays;

namespace Sense.Modules.Attendance.Application.Daily;

/// <summary>
/// Spec 5.4 status table: which daily lists one employee-day belongs to. An incalculable day (Z0.4) appears only in the
/// incalculable list among the record-derived lists; OnLeave comes from Leave, not from the record, so it is independent.
/// </summary>
public static class DailyStatusFilter
{
    public static IReadOnlySet<DailyStatus> Classify(AttendanceDay? day, bool onLeave)
    {
        var statuses = new HashSet<DailyStatus>();
        if (onLeave)
        {
            statuses.Add(DailyStatus.OnLeave);
        }

        if (day is null)
        {
            return statuses;
        }

        var flags = day.Anomalies;
        if (flags.HasFlag(AttendanceAnomaly.Incalculable))
        {
            statuses.Add(DailyStatus.Incalculable);
            return statuses;
        }

        AddIf(statuses, DailyStatus.Arrived, day.CheckIn is not null);
        AddIf(statuses, DailyStatus.Late, flags.HasFlag(AttendanceAnomaly.LateArrival));
        AddIf(statuses, DailyStatus.EarlyLeave, flags.HasFlag(AttendanceAnomaly.EarlyLeave));
        AddIf(statuses, DailyStatus.Absent, flags.HasFlag(AttendanceAnomaly.NoData) && !onLeave);
        AddIf(statuses, DailyStatus.Overtime, flags.HasFlag(AttendanceAnomaly.Overwork) || day.OvertimeMinutes > 0);
        AddIf(statuses, DailyStatus.Undertime, flags.HasFlag(AttendanceAnomaly.Underwork));
        return statuses;
    }

    private static void AddIf(HashSet<DailyStatus> statuses, DailyStatus status, bool condition)
    {
        if (condition)
        {
            statuses.Add(status);
        }
    }
}
