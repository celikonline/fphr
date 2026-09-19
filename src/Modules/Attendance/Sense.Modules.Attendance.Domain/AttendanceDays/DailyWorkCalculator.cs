using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Domain.AttendanceDays;

/// <summary>
/// Tenant-configured limits <see cref="AttendanceDay.Recompute"/> checks a day against (11.3.6 G-02): a snapshot of the
/// relevant <see cref="Shifts.ShiftSettings"/> fields, passed in by the caller (Application layer resolves the tenant's
/// row) so the Domain project never depends on a repository. <see cref="None"/> is the fallback used when no
/// <see cref="Shifts.ShiftSettings"/> row was resolved (mirrors <see cref="Shifts.ShiftSettings.CreateDefault"/>'s daily
/// limit, but with night work off - the same posture the previous behavior had before this type existed).
/// </summary>
public readonly record struct ShiftLimits(
    int MaxDailyMinutes, bool NightWorkEnabled, TimeOnly? NightWorkStart, TimeOnly? NightWorkEnd, int? NightWorkMaxMinutes)
{
    public static ShiftLimits None => new(ShiftSettings.DefaultMaxDailyMinutes, false, null, null, null);
}

/// <summary>Everything <see cref="DailyWorkCalculator.Calculate"/> needs for one day; <see cref="CheckIn"/>/<see cref="CheckOut"/> null means no attendance recorded.</summary>
public readonly record struct DailyWorkInput(
    TimeOnly? CheckIn, TimeOnly? CheckOut, bool Overnight, int ActualBreakMinutes, int PlannedMinutes, ShiftLimits Limits);

/// <summary>
/// Computed facts for one day (11.3.6 G-02 "puantaj hesaplama motoru"). All zero/false when the day has no
/// check-in/check-out. <see cref="OvertimeMinutes"/> is a **daily proxy** (worked − planned, floored at 0) - the
/// legally exact figure needs the weekly/denkleştirme view (<see cref="Equalization.EqualizationPeriod"/>) and is not
/// this type's job.
/// </summary>
public readonly record struct DailyWorkResult(
    int NetWorkedMinutes, int BreakMinutesApplied, int NightMinutes, bool DailyLimitExceeded, bool NightLimitExceeded, int OvertimeMinutes)
{
    public static DailyWorkResult Empty => new(0, 0, 0, false, false, 0);
}

/// <summary>
/// Pure, stateless puantaj hesaplama motoru (11.3.6 G-02, kart senseik-014). Turns a day's check-in/check-out plus the
/// mola/gece/limit inputs into the facts <see cref="AttendanceDay"/> stores. Kept free of any repository/DbContext
/// dependency on purpose - every input is a value the caller (Application layer) already resolved.
/// </summary>
public static class DailyWorkCalculator
{
    /// <summary>İş K. m.68 ara dinlenme eşiği: altında zorunlu mola yok.</summary>
    public const int BreakThresholdShortMinutes = 4 * 60;

    /// <summary>İş K. m.68: bu değere kadar (dahil) 30 dk, üzerinde 1 saat.</summary>
    public const int BreakThresholdLongMinutes = 450;

    public const int BreakMinutesShort = 30;

    public const int BreakMinutesLong = 60;

    /// <summary>
    /// m.68 yasal asgari ara dinlenme: &lt;4s → 0, 4s-7.5s (dahil) → 30dk, &gt;7.5s → 1sa. <paramref name="grossWorkedMinutes"/>
    /// is the raw check-in→check-out span (before any break is subtracted).
    /// </summary>
    public static int LegalMinimumBreakMinutes(int grossWorkedMinutes) =>
        grossWorkedMinutes switch
        {
            <= 0 => 0,
            < BreakThresholdShortMinutes => 0,
            <= BreakThresholdLongMinutes => BreakMinutesShort,
            _ => BreakMinutesLong,
        };

    public static DailyWorkResult Calculate(DailyWorkInput input)
    {
        if (input.CheckIn is null || input.CheckOut is null)
        {
            return DailyWorkResult.Empty;
        }

        var grossMinutes = (int)WorkedSpan(input.CheckIn.Value, input.CheckOut.Value, input.Overnight).TotalMinutes;

        // BreakMinutesApplied (m.68): the greater of what was actually observed (PDKS ara Out→In çiftleri) and the
        // legal minimum for that day's gross span - a shorter measured break never lets the employer count more
        // "worked" time than the law allows unpaid-break-free.
        var breakApplied = Math.Max(input.ActualBreakMinutes, LegalMinimumBreakMinutes(grossMinutes));
        var net = Math.Max(grossMinutes - breakApplied, 0);

        var nightMinutes = input.Limits.NightWorkEnabled && input.Limits.NightWorkStart is { } nightStart && input.Limits.NightWorkEnd is { } nightEnd
            ? Math.Min(NightOverlapMinutes(input.CheckIn.Value, grossMinutes, nightStart, nightEnd), net)
            : 0;

        var dailyLimitExceeded = input.Limits.MaxDailyMinutes > 0 && net > input.Limits.MaxDailyMinutes;
        var nightLimitExceeded = input.Limits.NightWorkEnabled && input.Limits.NightWorkMaxMinutes is { } maxNight && nightMinutes > maxNight;
        var overtimeMinutes = Math.Max(net - input.PlannedMinutes, 0);

        return new DailyWorkResult(net, breakApplied, nightMinutes, dailyLimitExceeded, nightLimitExceeded, overtimeMinutes);
    }

    /// <summary>Same wrap-past-midnight rule <see cref="AttendanceDay"/> already used for its legacy WorkedMinutes.</summary>
    public static TimeSpan WorkedSpan(TimeOnly checkIn, TimeOnly checkOut, bool overnight)
    {
        var span = checkOut.ToTimeSpan() - checkIn.ToTimeSpan();
        if (overnight && span <= TimeSpan.Zero)
        {
            span += TimeSpan.FromDays(1);
        }

        return span;
    }

    /// <summary>
    /// Minutes of [checkIn, checkIn + grossMinutes) that fall inside the (possibly midnight-wrapping, e.g. 20:00-06:00)
    /// night window, on any of the days the span touches. An anchor date is used only to get real <see cref="DateTime"/>
    /// arithmetic; the result depends solely on the time-of-day inputs.
    /// </summary>
    private static int NightOverlapMinutes(TimeOnly checkIn, int grossMinutes, TimeOnly nightStart, TimeOnly nightEnd)
    {
        var anchor = new DateOnly(2000, 1, 1);
        var start = anchor.ToDateTime(checkIn);
        var end = start.AddMinutes(grossMinutes);

        var total = 0;
        for (var day = anchor.AddDays(-1); day.ToDateTime(TimeOnly.MinValue) <= end; day = day.AddDays(1))
        {
            var windowStart = day.ToDateTime(nightStart);
            var windowEnd = nightEnd <= nightStart ? day.AddDays(1).ToDateTime(nightEnd) : day.ToDateTime(nightEnd);
            var overlapStart = start > windowStart ? start : windowStart;
            var overlapEnd = end < windowEnd ? end : windowEnd;
            if (overlapEnd > overlapStart)
            {
                total += (int)(overlapEnd - overlapStart).TotalMinutes;
            }
        }

        return total;
    }
}
