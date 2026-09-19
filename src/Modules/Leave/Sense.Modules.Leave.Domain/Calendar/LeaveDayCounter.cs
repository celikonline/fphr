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
/// UBGT izinden sayılmaz); calendar-day types (<c>CountsNonWorkingDays</c>) count every day. Half-day holidays are not
/// modelled yet.
/// </summary>
public static class LeaveDayCounter
{
    public static LeaveDayCount Count(
        DateOnly start,
        DateOnly end,
        Func<DateOnly, bool> isWorkingDay,
        IReadOnlyDictionary<DateOnly, string> holidays,
        bool countsNonWorkingDays,
        Func<DateOnly, bool>? includeSaturdayWhenFullWeek = null)
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

        // Calendar-day types already count every day; the Saturday rule only matters where weekly rest is excluded.
        if (!countsNonWorkingDays && includeSaturdayWhenFullWeek is not null)
        {
            ApplySaturdayRule(days, start, end, isWorkingDay, includeSaturdayWhenFullWeek, ref rest);
        }

        return new LeaveDayCount(days.Count(d => d.Counted), rest, holidayCount, days);
    }

    /// <summary>
    /// "Pazartesi'den Cuma'ya izin alırsa Cumartesi'yi ekle" (11.3.5/11.3.6 roadmap #23): for every calendar week
    /// (Mon–Sun) touching [start, end] whose Saturday is not a working day and whose schedule has the flag on, if
    /// every scheduled working day of that week's Monday–Friday falls inside [start, end], that Saturday counts as
    /// leave too - whether it was already inside the range (a multi-week span) or falls right after it (the common
    /// "just Mon–Fri requested" case, where it is appended to the breakdown past <paramref name="end"/>).
    /// A working day that happens to also be a public holiday still needs to be in range for the week to qualify -
    /// the spec leaves that combination as an explicit open point, defaulting to "the rule does not relax" (11.3.5 §Gün hesabı).
    /// </summary>
    private static void ApplySaturdayRule(
        List<LeaveDay> days, DateOnly start, DateOnly end, Func<DateOnly, bool> isWorkingDay, Func<DateOnly, bool> includeSaturdayWhenFullWeek, ref int rest)
    {
        for (var monday = MondayOf(start); monday <= end; monday = monday.AddDays(7))
        {
            var saturday = monday.AddDays(5);
            if (isWorkingDay(saturday) || !includeSaturdayWhenFullWeek(saturday))
            {
                continue;
            }

            var weekFullyRequested = true;
            for (var weekday = monday; weekday < saturday; weekday = weekday.AddDays(1))
            {
                if (isWorkingDay(weekday) && (weekday < start || weekday > end))
                {
                    weekFullyRequested = false;
                    break;
                }
            }

            if (!weekFullyRequested)
            {
                continue;
            }

            var existing = days.FindIndex(d => d.Date == saturday);
            if (existing >= 0)
            {
                if (!days[existing].Counted)
                {
                    days[existing] = days[existing] with { Counted = true };
                    rest--;
                }
            }
            else
            {
                days.Add(new LeaveDay(saturday, LeaveDayKind.WeeklyRest, Counted: true, HolidayName: null));
            }
        }

        days.Sort((a, b) => a.Date.CompareTo(b.Date));
    }

    /// <summary>Monday of the calendar week containing <paramref name="date"/> (the week the Saturday rule inspects).</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static bool MondayToFriday(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
