namespace Sense.Modules.Attendance.Domain.Equalization;

/// <summary>
/// Pure denkleştirme dönem hesapları (m.63, kart senseik-014 §4 - "en riskli parça"). Periods are fixed
/// <paramref name="periodMonths"/>-month blocks that tile forever from <paramref name="anchorMonth"/> (no partial
/// periods by construction - a mid-cycle settings change only affects periods computed *after* the change, since each
/// <see cref="EqualizationPeriod"/> row captures its own PeriodStart/PeriodEnd at open time).
/// </summary>
public static class EqualizationPeriodCalculator
{
    /// <summary>
    /// The (PeriodStart, PeriodEnd) block of <paramref name="periodMonths"/> calendar months that contains
    /// <paramref name="date"/>, given a cycle that starts on the 1st of <paramref name="anchorMonth"/> every year.
    /// </summary>
    public static (DateOnly PeriodStart, DateOnly PeriodEnd) GetPeriodBounds(DateOnly date, int periodMonths, int anchorMonth)
    {
        if (periodMonths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodMonths), periodMonths, "Period months must be positive.");
        }

        if (anchorMonth is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorMonth), anchorMonth, "Anchor month must be 1-12.");
        }

        var dateTotalMonths = date.Year * 12 + (date.Month - 1);
        var cycleAnchorTotalMonths = date.Month >= anchorMonth
            ? date.Year * 12 + (anchorMonth - 1)
            : (date.Year - 1) * 12 + (anchorMonth - 1);

        var offsetMonths = dateTotalMonths - cycleAnchorTotalMonths;
        var periodIndex = offsetMonths / periodMonths;
        var periodStartTotalMonths = cycleAnchorTotalMonths + (periodIndex * periodMonths);

        var periodStartYear = periodStartTotalMonths / 12;
        var periodStartMonth = periodStartTotalMonths % 12 + 1;
        var periodStart = new DateOnly(periodStartYear, periodStartMonth, 1);
        var periodEnd = periodStart.AddMonths(periodMonths).AddDays(-1);
        return (periodStart, periodEnd);
    }

    /// <summary>
    /// Number of ISO weeks that belong to [<paramref name="periodStart"/>, <paramref name="periodEnd"/>] under the
    /// implementation assumption "haftanın Pazartesi'si hangi dönemdeyse o dönem sayılır" (a week's Monday decides
    /// which period the whole week's budget belongs to) - PO/hukuk onayı bekleyen bir varsayım, bkz. handoff notu.
    /// </summary>
    public static int CountWeeksByMonday(DateOnly periodStart, DateOnly periodEnd)
    {
        var firstMonday = MondayOf(periodStart);
        if (firstMonday < periodStart)
        {
            firstMonday = firstMonday.AddDays(7);
        }

        var count = 0;
        for (var monday = firstMonday; monday <= periodEnd; monday = monday.AddDays(7))
        {
            count++;
        }

        return count;
    }

    private static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
