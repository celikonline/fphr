using Sense.Modules.Attendance.Domain.Punches;

namespace Sense.Modules.Attendance.Application.Punches;

/// <summary>
/// Taşımanın temsili mola çifti (<see cref="LegacyDayPunches.BreakMarkerPrefix"/>) yalnız günde başka ara okutma yokken sayılır: gerçek
/// ara okutmalar (ilk giriş / son çıkış dışındaki işaretsiz okutmalar) geldiyse yok sayılır - mola ikiye katlanmaz, sıra bozulmaz.
/// Gün hesabı (<see cref="DayFromPunches"/>) ve mola/ara kayıt raporları aynı kuralı buradan kullanır.
/// </summary>
public static class BackfillBreakFilter
{
    private const int FirstInAndLastOut = 2;

    /// <summary>Zaman sırasındaki silinmemiş okutmalar verilir; geçersiz kılınan işaretli çift çıkarılmış liste döner.</summary>
    public static List<Punch> WithoutSuperseded(IReadOnlyList<Punch> live)
    {
        var real = live.Where(p => !IsBackfillBreak(p)).ToList();
        return real.Count != live.Count && real.Count > FirstInAndLastOut ? real : [.. live];
    }

    public static bool IsBackfillBreak(Punch punch) =>
        punch.SourceRef?.StartsWith(LegacyDayPunches.BreakMarkerPrefix, StringComparison.Ordinal) == true;
}
