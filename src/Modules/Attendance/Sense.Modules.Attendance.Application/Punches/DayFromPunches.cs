using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Okutmalardan türetilen gün özeti - <c>AttendanceDay.Record/UpdateRecord</c>'a aynen verilir. <see cref="Overnight"/> son çıkışın ilk
/// girişten sonraki takvim gününde olduğunu (gece vardiyası) söyler.
/// </summary>
public sealed record DayResult(TimeOnly? CheckIn, TimeOnly? CheckOut, int ActualBreakMinutes, bool Incalculable, bool Overnight = false);

/// <summary>
/// Gün hesabı (spec §5.1, Z0.1/Z0.4): silinmemiş okutmalar zamana göre sıralanır; <c>CheckIn</c> = ilk In, <c>CheckOut</c> = son Out,
/// aradaki her Out→In boşluğu gerçek molaya (<see cref="DayResult.ActualBreakMinutes"/>) eklenir. Yönsüz (Unknown) okutmalar çift
/// sayıdaysa sıraya göre In/Out yorumlanır.
/// Hesaplanamaz (<see cref="DayResult.Incalculable"/>): tek sayıda okutma içinde yönsüz olan var; iki ya da daha fazla okutmada yön sırası
/// In,Out,In,Out… değil (In,In / ilk kayıt Out / tek sayı); okutma var ama hiç plan yok (<c>plan == null</c>).
/// Tek yönlü tek okutma (yalnız In ya da yalnız Out) hesaplanamaz sayılmaz - mevcut eksik giriş/çıkış bayrakları onu kapsar.
/// </summary>
public static class DayFromPunches
{
    private static readonly DayResult Empty = new(null, null, 0, Incalculable: false, Overnight: false);

    public static DayResult Build(IReadOnlyList<Punch> punches, PlannedDay? plan, TimeZoneInfo? zone = null)
    {
        var live = punches.Where(p => !p.IsDeleted).OrderBy(p => p.OccurredAtUtc).ToList();
        if (live.Count == 0)
        {
            return Empty;
        }

        var times = live.Select(p => PunchTime.ToLocal(p.OccurredAtUtc, zone)).ToList();
        var directions = ResolveDirections(live);
        if (plan is null || directions is null)
        {
            return IncalculableDay(times, directions ?? live.Select(p => p.Direction).ToList());
        }

        if (live.Count == 1)
        {
            var only = TimeOnly.FromDateTime(times[0]);
            return directions[0] == PunchDirection.In ? Empty with { CheckIn = only } : Empty with { CheckOut = only };
        }

        return IsInOutSequence(directions) ? PairedDay(times) : IncalculableDay(times, directions);
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

    private static DayResult PairedDay(List<DateTime> times)
    {
        var breakMinutes = 0;
        for (var i = 1; i < times.Count - 1; i += 2)
        {
            breakMinutes += (int)(times[i + 1] - times[i]).TotalMinutes;
        }

        return new DayResult(
            TimeOnly.FromDateTime(times[0]), TimeOnly.FromDateTime(times[^1]), breakMinutes, Incalculable: false, Overnight: IsOvernight(times[0], times[^1]));
    }

    /// <summary>Gösterim için ilk In / son Out korunur; son Out ilk In'den önceyse (bozuk sıra) çıkış boş bırakılır.</summary>
    private static DayResult IncalculableDay(List<DateTime> times, List<PunchDirection> directions)
    {
        var firstIn = directions.IndexOf(PunchDirection.In);
        var lastOut = directions.LastIndexOf(PunchDirection.Out);
        DateTime? checkIn = firstIn >= 0 ? times[firstIn] : null;
        DateTime? checkOut = lastOut >= 0 && (checkIn is null || times[lastOut] > checkIn) ? times[lastOut] : null;
        return new DayResult(
            checkIn is { } i ? TimeOnly.FromDateTime(i) : null, checkOut is { } o ? TimeOnly.FromDateTime(o) : null, 0, Incalculable: true,
            Overnight: checkIn is { } a && checkOut is { } b && IsOvernight(a, b));
    }

    private static bool IsOvernight(DateTime first, DateTime last) => DateOnly.FromDateTime(last) > DateOnly.FromDateTime(first);
}
