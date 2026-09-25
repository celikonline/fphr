using Sense.Modules.Attendance.Domain.Punches;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary><see cref="WorkDateResolver"/> sonucu; <see cref="Ambiguous"/> gece vardiyası çakışmasıdır (gün Incalculable olur).</summary>
public readonly record struct WorkDateResolution(DateOnly Date, bool Ambiguous);

/// <summary>
/// Okutmanın ait olduğu puantaj günü (spec §5.1): aday günlerin plan penceresine (vardiya başlangıcı − 4 sa, bitişi + 4 sa; gece ve
/// <see cref="PlannedDay.StartsPreviousDay"/> dahil) düşüyorsa o planın günü; birden çoğuna düşüyorsa başlangıcı yakın olan (eşitse
/// belirsiz - en erken aday gün döner, <see cref="WorkDateResolution.Ambiguous"/> işaretli); hiçbirine düşmüyorsa yerel takvim günü.
/// </summary>
public static class WorkDateResolver
{
    public const int WindowPaddingHours = 4;

    private static readonly TimeSpan Padding = TimeSpan.FromHours(WindowPaddingHours);

    public static DateOnly Resolve(Punch punch, IReadOnlyList<(DateOnly Date, PlannedDay? Plan)> candidates, TimeZoneInfo? zone = null) =>
        ResolveDetailed(PunchTime.ToLocal(punch.OccurredAtUtc, zone), candidates).Date;

    /// <param name="local">Okutma anı, kiracının yerel duvar saatiyle.</param>
    public static WorkDateResolution ResolveDetailed(DateTime local, IReadOnlyList<(DateOnly Date, PlannedDay? Plan)> candidates)
    {
        var hits = candidates
            .Select(c => (c.Date, Bounds: c.Plan?.ShiftBounds(c.Date)))
            .Where(c => c.Bounds is { } b && local >= b.Start - Padding && local <= b.End + Padding)
            .Select(c => (c.Date, Distance: (local - c.Bounds!.Value.Start).Duration()))
            .OrderBy(c => c.Distance)
            .ThenBy(c => c.Date)
            .ToList();

        if (hits.Count == 0)
        {
            return new WorkDateResolution(DateOnly.FromDateTime(local), Ambiguous: false);
        }

        var ambiguous = hits.Count > 1 && hits[1].Distance == hits[0].Distance;
        return new WorkDateResolution(hits[0].Date, ambiguous);
    }
}
