namespace Sense.Modules.Leave.Domain.Calendar;

/// <summary>
/// Fractional-day calculation for <see cref="RuleSets.LeaveType.Unit"/> = HalfDay/Hourly requests (always a single
/// calendar day - see <see cref="Requests.LeaveRequest"/> validation). Hourly follows the same "until the work
/// schedule contract carries per-employee daily hours" convention as <c>Overtime.OvertimeMath.DefaultDailyWorkMinutes</c>
/// (4857 md.63: 45 h/week ÷ 6 gün = 7.5 h/gün) so both leave and overtime agree on what "one day" is worth in minutes.
/// </summary>
public static class LeavePartialDay
{
    /// <summary>A half-day request always consumes exactly half a balance day, regardless of which half.</summary>
    public const decimal HalfDayFraction = 0.5m;

    /// <summary>Same fallback as OvertimeMath.DefaultDailyWorkMinutes; kept as a separate constant so the two modules can diverge later.</summary>
    public const int DefaultDailyWorkMinutes = 450;

    /// <summary>Requested minutes ÷ a work day's minutes, rounded to the ledger's 4-decimal precision.</summary>
    public static decimal HourlyFraction(TimeOnly start, TimeOnly end, int dailyWorkMinutes = DefaultDailyWorkMinutes)
    {
        var minutes = (decimal)(end - start).TotalMinutes;
        return decimal.Round(minutes / dailyWorkMinutes, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A flat statutory hour count (e.g. "günde en az 2 saat iş arama izni", 4857 md.27; "günde 1,5 saat süt izni",
    /// md.74/son) expressed as the same day-fraction <see cref="HourlyFraction"/> produces, so it can be compared
    /// directly against <see cref="RuleSets.LeaveTypeSettings.MinConsecutiveDays"/>/<see cref="RuleSets.LeaveTypeSettings.MaxConsecutiveDays"/>.
    /// </summary>
    public static decimal HoursFraction(decimal hours, int dailyWorkMinutes = DefaultDailyWorkMinutes) =>
        decimal.Round(hours * 60m / dailyWorkMinutes, 4, MidpointRounding.AwayFromZero);
}
