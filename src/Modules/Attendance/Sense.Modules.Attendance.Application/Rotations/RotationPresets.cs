using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.Rotations;

/// <summary>Hazır kalıbın döngü uzunluğu ve adımları; <see cref="RotationPattern.Create"/> girdisi olarak kullanılır.</summary>
public sealed record RotationPresetDefinition(RotationPreset Preset, int CycleLengthDays, IReadOnlyList<RotationStep> Steps);

/// <summary>Hazır rotasyon kalıpları (karar): dinlenme günleri <see cref="ShiftDayType.WeeklyRest"/>, vardiyalar slot sırasıyla verilir.</summary>
public static class RotationPresets
{
    /// <summary>2×12: [gündüz, gece, dinlenme, dinlenme] - döngü 4.</summary>
    public static RotationPresetDefinition TwoByTwelve(Guid dayShiftId, Guid nightShiftId) =>
        Build(RotationPreset.TwoByTwelve, [dayShiftId, nightShiftId], restDays: 2);

    /// <summary>3×8: [sabah, akşam, gece, dinlenme] - döngü 4.</summary>
    public static RotationPresetDefinition ThreeByEight(Guid morningShiftId, Guid eveningShiftId, Guid nightShiftId) =>
        Build(RotationPreset.ThreeByEight, [morningShiftId, eveningShiftId, nightShiftId], restDays: 1);

    /// <summary>4×6: [v1, v2, v3, v4, dinlenme] - döngü 5.</summary>
    public static RotationPresetDefinition FourBySix(Guid shift1Id, Guid shift2Id, Guid shift3Id, Guid shift4Id) =>
        Build(RotationPreset.FourBySix, [shift1Id, shift2Id, shift3Id, shift4Id], restDays: 1);

    private static RotationPresetDefinition Build(RotationPreset preset, Guid[] shifts, int restDays)
    {
        var steps = new List<RotationStep>();
        foreach (var shiftId in shifts)
        {
            steps.Add(RotationStep.Of(steps.Count, shiftId, ShiftDayType.Work));
        }

        for (var i = 0; i < restDays; i++)
        {
            steps.Add(RotationStep.Of(steps.Count, null, ShiftDayType.WeeklyRest));
        }

        return new RotationPresetDefinition(preset, steps.Count, steps);
    }
}
