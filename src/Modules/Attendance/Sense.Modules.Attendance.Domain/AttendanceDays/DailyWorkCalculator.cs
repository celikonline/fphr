using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Domain.AttendanceDays;

/// <summary>
/// Tenant-configured limits <see cref="AttendanceDay.Recompute"/> checks a day against (11.3.6 G-02): a snapshot of the
/// relevant <see cref="Shifts.ShiftSettings"/> fields, passed in by the caller (Application layer resolves the tenant's
/// row) so the Domain project never depends on a repository. <see cref="None"/> is the fallback used when no
/// <see cref="Shifts.ShiftSettings"/> row was resolved (mirrors <see cref="Shifts.ShiftSettings.CreateDefault"/>'s daily
/// limit, but with night work off - the same posture the previous behavior had before this type existed).
/// <see cref="Rules"/> is the day's <see cref="ShiftDefinition"/> rule set (Z0.2), null when the plan has none (legacy behavior).
/// </summary>
public readonly record struct ShiftLimits(
    int MaxDailyMinutes, bool NightWorkEnabled, TimeOnly? NightWorkStart, TimeOnly? NightWorkEnd, int? NightWorkMaxMinutes, ShiftRules? Rules = null)
{
    public static ShiftLimits None => new(ShiftSettings.DefaultMaxDailyMinutes, false, null, null, null);
}

/// <summary>
/// Vardiya tanımı kural seti (zaman-devam Z0.2, spec §5.2): geç kalma / erken çıkma toleransı, giriş (erken gelme) ve çıkış
/// (geç kalma) tarafı fazla mesai eşikleri (min altı atılır, maks'ta kesilir; null = eşik/tavan yok) ve net çalışmaya eklenen
/// bonus süre. Snapshot of the <see cref="ShiftDefinition"/> fields at calculation time.
/// </summary>
public readonly record struct ShiftRules(
    int LateToleranceMinutes,
    int EarlyLeaveToleranceMinutes,
    int? EarlyArrivalOvertimeMinMinutes,
    int? EarlyArrivalOvertimeMaxMinutes,
    int? LateDepartureOvertimeMinMinutes,
    int? LateDepartureOvertimeMaxMinutes,
    int BonusMinutes)
{
    public static ShiftRules From(ShiftDefinition definition) =>
        new(definition.LateToleranceMinutes, definition.EarlyLeaveToleranceMinutes, definition.EarlyArrivalOvertimeMinMinutes,
            definition.EarlyArrivalOvertimeMaxMinutes, definition.LateDepartureOvertimeMinMinutes, definition.LateDepartureOvertimeMaxMinutes,
            definition.BonusMinutes);
}

/// <summary>
/// Everything <see cref="DailyWorkCalculator.Calculate"/> needs for one day; <see cref="CheckIn"/>/<see cref="CheckOut"/> null means no
/// attendance recorded. <see cref="PlannedStart"/>/<see cref="PlannedEnd"/> are only used by the rule-based (per side) overtime.
/// </summary>
public readonly record struct DailyWorkInput(
    TimeOnly? CheckIn, TimeOnly? CheckOut, bool Overnight, int ActualBreakMinutes, int PlannedMinutes, ShiftLimits Limits,
    TimeOnly? PlannedStart = null, TimeOnly? PlannedEnd = null);

/// <summary>
/// Computed facts for one day (11.3.6 G-02 "puantaj hesaplama motoru"). All zero/false when the day has no
/// check-in/check-out. <see cref="OvertimeMinutes"/> is a **daily proxy** (worked − planned, floored at 0; with
/// <see cref="ShiftRules"/> the sum of the early-arrival and late-departure sides after their min/max) - the legally exact
/// figure needs the weekly/denkleştirme view (<see cref="Equalization.EqualizationPeriod"/>) and is not this type's job.
/// <see cref="NetWorkedMinutes"/> includes <see cref="ShiftRules.BonusMinutes"/>; limit checks use the actually worked time.
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
        var overtimeMinutes = input.Limits.Rules is { } rules && input.PlannedStart is { } plannedStart && input.PlannedEnd is { } plannedEnd
            ? RuleOvertimeMinutes(input.CheckIn.Value, grossMinutes, plannedStart, plannedEnd, rules)
            : Math.Max(net - input.PlannedMinutes, 0);
        var bonusMinutes = input.Limits.Rules?.BonusMinutes ?? 0;

        return new DailyWorkResult(net + bonusMinutes, breakApplied, nightMinutes, dailyLimitExceeded, nightLimitExceeded, overtimeMinutes);
    }

    /// <summary>
    /// Geç kalma (spec §5.2): <c>CheckIn &gt; start + LateTolerance</c>. Without rules the tolerance is 0, which is exactly the
    /// legacy <c>checkIn &gt; plannedStart</c> comparison (same-day time-of-day arithmetic, no midnight wrap).
    /// </summary>
    public static bool IsLateArrival(TimeOnly? checkIn, TimeOnly? plannedStart, ShiftRules? rules) =>
        checkIn is { } actual && plannedStart is { } planned
        && (actual.ToTimeSpan() - planned.ToTimeSpan()).TotalMinutes > (rules?.LateToleranceMinutes ?? 0);

    /// <summary>Erken çıkma (spec §5.2): <c>CheckOut &lt; end − EarlyLeaveTolerance</c>; tolerance 0 without rules (legacy comparison).</summary>
    public static bool IsEarlyLeave(TimeOnly? checkOut, TimeOnly? plannedEnd, ShiftRules? rules) =>
        checkOut is { } actual && plannedEnd is { } planned
        && (planned.ToTimeSpan() - actual.ToTimeSpan()).TotalMinutes > (rules?.EarlyLeaveToleranceMinutes ?? 0);

    /// <summary>
    /// Kural setli fazla mesai: giriş tarafı (plandan erken gelinen dakikalar) ve çıkış tarafı (plandan sonra kalınan dakikalar)
    /// ayrı sayılır; bir taraf min eşiğinin altındaysa atılır, maks'ı aşıyorsa kesilir. Offsets are measured on a timeline
    /// anchored at the planned start (check-in within ±12h of it), so night shifts crossing midnight work too.
    /// </summary>
    public static int RuleOvertimeMinutes(TimeOnly checkIn, int grossMinutes, TimeOnly plannedStart, TimeOnly plannedEnd, ShiftRules rules)
    {
        var checkInOffset = SignedMinutesBetween(plannedStart, checkIn);
        var plannedLength = WrappedMinutesBetween(plannedStart, plannedEnd);
        var earlyArrival = Math.Max(-checkInOffset, 0);
        var lateDeparture = Math.Max(checkInOffset + grossMinutes - plannedLength, 0);
        return CountedSide(earlyArrival, rules.EarlyArrivalOvertimeMinMinutes, rules.EarlyArrivalOvertimeMaxMinutes)
            + CountedSide(lateDeparture, rules.LateDepartureOvertimeMinMinutes, rules.LateDepartureOvertimeMaxMinutes);
    }

    private static int CountedSide(int excessMinutes, int? minMinutes, int? maxMinutes) =>
        excessMinutes <= 0 || excessMinutes < (minMinutes ?? 0) ? 0 : Math.Min(excessMinutes, maxMinutes ?? int.MaxValue);

    /// <summary><paramref name="to"/> − <paramref name="from"/> folded into (−720, 720] minutes.</summary>
    private static int SignedMinutesBetween(TimeOnly from, TimeOnly to)
    {
        var diff = (int)(to.ToTimeSpan() - from.ToTimeSpan()).TotalMinutes;
        var half = AttendanceLimits.MaxMinutesPerDay / 2;
        return diff > half ? diff - AttendanceLimits.MaxMinutesPerDay : diff <= -half ? diff + AttendanceLimits.MaxMinutesPerDay : diff;
    }

    /// <summary>Planned span in (0, 1440] minutes; an end at or before the start crosses midnight.</summary>
    private static int WrappedMinutesBetween(TimeOnly start, TimeOnly end)
    {
        var diff = (int)(end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        return diff <= 0 ? diff + AttendanceLimits.MaxMinutesPerDay : diff;
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
    public static int NightOverlapMinutes(TimeOnly checkIn, int grossMinutes, TimeOnly nightStart, TimeOnly nightEnd)
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
