using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.ShiftDefinitions;

/// <summary>Result of <see cref="DynamicShiftMatcher.Match"/>: a single closest definition, or none when two or more tie (belirsiz).</summary>
public sealed record DynamicShiftMatch(ShiftDefinition? Definition, bool IsAmbiguous)
{
    public static DynamicShiftMatch None => new(null, false);
}

/// <summary>
/// Dinamik vardiya (K1): günün ilk girişine başlangıcı en yakın aktif dinamik <see cref="ShiftDefinition"/> seçilir. Mesafe gün
/// çevresinde ölçülür (23:50 ile 00:10 arası 20 dk). Aday küme: çalışana atanmış vardiya grubu kavramı olmadığından tüm aktif
/// dinamik tanımlar. Eşit uzaklıkta birden çok aday belirsizdir (spec §9: belirsizlikte seçim yapılmaz).
/// </summary>
public static class DynamicShiftMatcher
{
    public static DynamicShiftMatch Match(IEnumerable<ShiftDefinition> definitions, TimeOnly firstIn)
    {
        var firstInMinute = (firstIn.Hour * 60) + firstIn.Minute;
        var ranked = definitions
            .Where(d => d.IsActive && d.Type == ShiftType.Dynamic)
            .Select(d => (Definition: d, Distance: CircularDistance(d.StartMinute, firstInMinute)))
            .OrderBy(x => x.Distance)
            .Take(2)
            .ToList();

        if (ranked.Count == 0)
        {
            return DynamicShiftMatch.None;
        }

        return ranked.Count > 1 && ranked[0].Distance == ranked[1].Distance
            ? new DynamicShiftMatch(null, true)
            : new DynamicShiftMatch(ranked[0].Definition, false);
    }

    private static int CircularDistance(int a, int b)
    {
        var diff = Math.Abs(a - b);
        return Math.Min(diff, AttendanceLimits.MaxMinutesPerDay - diff);
    }
}
