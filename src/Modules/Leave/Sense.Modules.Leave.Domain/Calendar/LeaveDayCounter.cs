namespace Sense.Modules.Leave.Domain.Calendar;

public enum LeaveDayKind
{
    Workday = 0,
    WeeklyRest = 1,
    PublicHoliday = 2,
}

/// <summary>One day of a leave calculation breakdown ("CalculationBreakdown").</summary>
public sealed record LeaveDay(DateOnly Date, LeaveDayKind Kind, bool Counted, string? HolidayName);

public sealed record LeaveDayCount(decimal Days, int WeeklyRestDays, int PublicHolidayDays, IReadOnlyList<LeaveDay> Breakdown);

/// <summary>
/// Counts leave days in [start, end]: working days exclude weekly rest days and public holidays (md.53 - hafta tatili ve
/// UBGT izinden sayılmaz); calendar-day types (<c>CountsNonWorkingDays</c>) count every day. Half-day holidays and the
/// "Pzt–Cuma → Cumartesi ekle" schedule rule need the Attendance work schedule contract (roadmap #23) and are not modelled yet.
/// </summary>
public static class LeaveDayCounter
{
    public static LeaveDayCount Count(
        DateOnly start, DateOnly end, Func<DateOnly, bool> isWorkingDay, IReadOnlyDictionary<DateOnly, string> holidays, bool countsNonWorkingDays)
    {
        var days = new List<LeaveDay>();
        var rest = 0;
        var holidayCount = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var kind = holidays.ContainsKey(date) ? LeaveDayKind.PublicHoliday : isWorkingDay(date) ? LeaveDayKind.Workday : LeaveDayKind.WeeklyRest;
            var counted = countsNonWorkingDays || kind == LeaveDayKind.Workday;
            if (!counted && kind == LeaveDayKind.WeeklyRest)
            {
                rest++;
            }
            else if (!counted)
            {
                holidayCount++;
            }

            days.Add(new LeaveDay(date, kind, counted, holidays.GetValueOrDefault(date)));
        }

        return new LeaveDayCount(days.Count(d => d.Counted), rest, holidayCount, days);
    }

    public static bool MondayToFriday(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
