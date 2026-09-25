using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.Rotations;

/// <summary>
/// Rotasyon kalıbını takvim günlerine açan saf fonksiyon (Z3.3). <paramref name="start"/> gününde döngü
/// <paramref name="offsetDays"/>. adımdan başlar; [from, to] aralığının <paramref name="start"/> öncesi günleri üretilmez.
/// Adımı olmayan döngü günü plansızdır ve sonuca girmez. Bitiş tarihi çağıran tarafından <paramref name="to"/> ile uygulanır.
/// </summary>
public static class RotationExpander
{
    public static IReadOnlyList<(DateOnly Date, Guid? ShiftDefinitionId, ShiftDayType DayType)> Expand(
        RotationPattern pattern, DateOnly start, int offsetDays, DateOnly from, DateOnly to)
    {
        var steps = pattern.Steps.ToDictionary(s => s.DayOffset);
        var cycle = pattern.CycleLengthDays;
        var result = new List<(DateOnly, Guid?, ShiftDayType)>();
        for (var date = from > start ? from : start; date <= to; date = date.AddDays(1))
        {
            var index = (((date.DayNumber - start.DayNumber) + offsetDays) % cycle + cycle) % cycle;
            if (steps.TryGetValue(index, out var step))
            {
                result.Add((date, step.ShiftDefinitionId, step.DayType));
            }
        }

        return result;
    }
}
