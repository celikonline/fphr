using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutmalardan türetilen gün özeti - <c>AttendanceDay.Record/UpdateRecord</c>'a aynen verilir. <see cref="Overnight"/> son çıkışın ilk
/// girişten sonraki takvim gününde olduğunu (gece vardiyası) söyler.
/// </summary>
public sealed record DayResult(
    TimeOnly? CheckIn, TimeOnly? CheckOut, int ActualBreakMinutes, bool Incalculable, bool Overnight = false, IncalculableReason Reason = IncalculableReason.None);

/// <summary>
/// Gün hesabı (spec §5.1, Z0.1/Z0.4): silinmemiş okutmalar zamana göre sıralanır; <c>CheckIn</c> = ilk In, <c>CheckOut</c> = son Out,
/// aradaki her Out→In boşluğu gerçek molaya (<see cref="DayResult.ActualBreakMinutes"/>) eklenir. Yönsüz (Unknown) okutmalar çift
/// sayıdaysa sıraya göre In/Out yorumlanır. Self-servis mola okutmaları (<see cref="PunchDirection.BreakStart"/>/<see cref="PunchDirection.BreakEnd"/>)
/// giriş/çıkışı hiç değiştirmez ve In/Out sırasına katılmaz: her mola başı bir sonraki mola sonuna ya da çıkışa (Out) kadar süren molayı
/// açar ve süresi gerçek molaya eklenir; açık (kapanmamış) mola eklenmez.
/// Hesaplanamaz (<see cref="DayResult.Incalculable"/>): tek sayıda okutma içinde yönsüz olan var; iki ya da daha fazla okutmada yön sırası
/// In,Out,In,Out… değil (In,In / ilk kayıt Out / tek sayı); okutma var ama hiç plan yok (<c>plan == null</c>).
/// Tek yönlü tek okutma (yalnız In ya da yalnız Out) hesaplanamaz sayılmaz - mevcut eksik giriş/çıkış bayrakları onu kapsar (İNSAN KARARI).
/// Hesaplanamayan günün nedeni <see cref="DayResult.Reason"/>'dadır (ör. elle + cihaz kanal çakışması).
/// </summary>
public static class DayFromPunches
{
    private static readonly DayResult Empty = new(null, null, 0, Incalculable: false, Overnight: false);

    /// <param name="plan">Günün K2 planı (<see cref="IPlannedDayResolver"/>); <c>null</c> = hiç plan yok (okutma varsa Incalculable).</param>
    public static DayResult Build(IReadOnlyList<Punch> punches, PlannedDay? plan, TimeZoneInfo? zone = null)
    {
        var ordered = BackfillBreakFilter.WithoutSuperseded(punches.Where(p => !p.IsDeleted).OrderBy(p => p.OccurredAtUtc).ToList());
        var live = ordered.Where(p => !IsBreak(p.Direction)).ToList();
        if (live.Count == 0)
        {
            return Empty;
        }

        var selfServiceBreak = SelfServiceBreaks(ordered).Sum(b => (int)b.TotalMinutes);

        var times = live.Select(p => PunchTime.ToLocal(p.OccurredAtUtc, zone)).ToList();
        var directions = ResolveDirections(live);
        if (directions is null)
        {
            return IncalculableDay(times, live.Select(p => p.Direction).ToList(), IncalculableReason.UnknownDirection);
        }

        if (plan is null)
        {
            return IncalculableDay(times, directions, IncalculableReason.NoPlan);
        }

        if (live.Count == 1)
        {
            var only = TimeOnly.FromDateTime(times[0]);
            var single = directions[0] == PunchDirection.In ? Empty with { CheckIn = only } : Empty with { CheckOut = only };
            return single with { ActualBreakMinutes = selfServiceBreak };
        }

        return IsInOutSequence(directions)
            ? PairedDay(times, selfServiceBreak)
            : IncalculableDay(times, directions, SequenceReason(live));
    }

    /// <summary>Self-servis mola başı/sonu okutması mı (giriş/çıkış sırasına katılmaz).</summary>
    public static bool IsBreak(PunchDirection direction) => direction is PunchDirection.BreakStart or PunchDirection.BreakEnd;

    /// <summary>
    /// Zaman sırasındaki okutmalardan self-servis molaları: mola başı, ardından gelen ilk mola sonu ya da çıkışla (Out) kapanır; açık mola
    /// ve eşi olmayan mola sonu sayılmaz (mola başı/sonu self-servis uç noktasının kuralı, SelfCheckInBreaks).
    /// </summary>
    public static List<TimeSpan> SelfServiceBreaks(IReadOnlyList<Punch> ordered)
    {
        var breaks = new List<TimeSpan>();
        DateTime? openStart = null;
        foreach (var punch in ordered)
        {
            if (punch.Direction == PunchDirection.BreakStart)
            {
                openStart ??= punch.OccurredAtUtc;
            }
            else if (punch.Direction is PunchDirection.BreakEnd or PunchDirection.Out && openStart is { } start)
            {
                breaks.Add(punch.OccurredAtUtc - start);
                openStart = null;
            }
        }

        return breaks;
    }

    /// <summary>
    /// Günün ilk girişi (yerel saat) - plan çözümlemesinin girdisi (esnek/dinamik vardiya, K1). Plandan bağımsızdır: yön sırası bozuk olsa da
    /// ilk "In" döner; yönler çözülemiyorsa null.
    /// </summary>
    public static TimeOnly? FirstIn(IReadOnlyList<Punch> punches, TimeZoneInfo? zone = null) =>
        Build(punches, PlannedDay.NonWorking(), zone).CheckIn;

    /// <summary>Bozuk sıra gün düzeyi bir kanalın (elle / Excel) okutmalarıyla başka bir kanalınkinin karışmasından geliyorsa kanal çakışması.</summary>
    private static IncalculableReason SequenceReason(List<Punch> live)
    {
        var sources = live.Select(p => p.Source).Distinct().ToList();
        var hasDayLevel = sources.Any(s => s is PunchSource.Manual or PunchSource.Import);
        return sources.Count > 1 && hasDayLevel ? IncalculableReason.ChannelConflict : IncalculableReason.BrokenSequence;
    }

    /// <summary>Yönsüz okutmaları sıraya göre yorumlar; tek sayıda okutmada yönsüz olan varsa null (hesaplanamaz).</summary>
    private static List<PunchDirection>? ResolveDirections(List<Punch> live)
    {
        if (live.Count % 2 != 0 && live.Any(p => p.Direction == PunchDirection.Unknown))
        {
            return null;
        }

        var resolved = new List<PunchDirection>(live.Count);
        foreach (var punch in live)
        {
            var expected = resolved.Count == 0 || resolved[^1] == PunchDirection.Out ? PunchDirection.In : PunchDirection.Out;
            resolved.Add(punch.Direction == PunchDirection.Unknown ? expected : punch.Direction);
        }

        return resolved;
    }

    private static bool IsInOutSequence(List<PunchDirection> directions) =>
        directions.Count % 2 == 0 && directions.Select((d, i) => d == (i % 2 == 0 ? PunchDirection.In : PunchDirection.Out)).All(ok => ok);

    private static DayResult PairedDay(List<DateTime> times, int selfServiceBreakMinutes)
    {
        var breakMinutes = selfServiceBreakMinutes;
        for (var i = 1; i < times.Count - 1; i += 2)
        {
            breakMinutes += (int)(times[i + 1] - times[i]).TotalMinutes;
        }

        return new DayResult(
            TimeOnly.FromDateTime(times[0]), TimeOnly.FromDateTime(times[^1]), breakMinutes, Incalculable: false, Overnight: IsOvernight(times[0], times[^1]));
    }

    /// <summary>Gösterim için ilk In / son Out korunur; son Out ilk In'den önceyse (bozuk sıra) çıkış boş bırakılır.</summary>
    private static DayResult IncalculableDay(List<DateTime> times, List<PunchDirection> directions, IncalculableReason reason)
    {
        var firstIn = directions.IndexOf(PunchDirection.In);
        var lastOut = directions.LastIndexOf(PunchDirection.Out);
        DateTime? checkIn = firstIn >= 0 ? times[firstIn] : null;
        DateTime? checkOut = lastOut >= 0 && (checkIn is null || times[lastOut] > checkIn) ? times[lastOut] : null;
        return new DayResult(
            checkIn is { } i ? TimeOnly.FromDateTime(i) : null, checkOut is { } o ? TimeOnly.FromDateTime(o) : null, 0, Incalculable: true,
            Overnight: checkIn is { } a && checkOut is { } b && IsOvernight(a, b), Reason: reason);
    }

    private static bool IsOvernight(DateTime first, DateTime last) => DateOnly.FromDateTime(last) > DateOnly.FromDateTime(first);
}
