using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Application.ShiftDefinitions;

/// <summary>
/// Turns a <see cref="ShiftDefinition"/> (or a legacy hours-only <see cref="ShiftAssignment"/>) into the <see cref="PlannedDay"/>
/// the day calculation snapshots (spec §5.2, K1). Pure: no repository access.
/// </summary>
public static class ShiftRuleEvaluator
{
    /// <summary>
    /// Sabit/dinamik: tanımın saatleri. Esnek (K1): planlanan giriş = gerçek giriş, pencere dışındaysa pencereye kırpılır
    /// (giriş yoksa tanımın başlangıcı kırpılır); bitiş = giriş + süre. Mola listesinin toplamı planlanan molayı belirler.
    /// </summary>
    public static PlannedDay Plan(ShiftDefinition definition, TimeOnly? firstIn)
    {
        var isFlexible = definition.Type == ShiftType.Flexible;
        var startMinute = isFlexible ? FlexibleStartMinute(definition, firstIn) : definition.StartMinute;
        var lengthMinutes = isFlexible && definition.DurationMinutes is { } duration
            ? duration
            : WrappedLength(definition.StartMinute, definition.EndMinute);
        var breakMinutes = definition.Breaks.Sum(b => b.DurationMinutes);

        return new PlannedDay(
            Start: ToTime(startMinute), End: ToTime(startMinute + lengthMinutes), PlannedMinutes: Math.Max(lengthMinutes - breakMinutes, 0),
            BreakMinutes: breakMinutes, StartsPreviousDay: definition.StartsPreviousDay, Rules: ShiftRules.From(definition), ShiftDefinitionId: definition.Id);
    }

    /// <summary>Tanımsız (eski) atama: yalnız satırın kendi saatleri ve molası, kural seti yok.</summary>
    public static PlannedDay FromAssignment(ShiftAssignment assignment) =>
        new(Start: assignment.Start, End: assignment.End, PlannedMinutes: assignment.ShiftMinutes, BreakMinutes: assignment.BreakMinutes);

    private static int FlexibleStartMinute(ShiftDefinition definition, TimeOnly? firstIn)
    {
        var anchor = firstIn is { } actual ? (actual.Hour * 60) + actual.Minute : definition.StartMinute;
        var earliest = definition.FlexStartEarliestMinute ?? anchor;
        var latest = definition.FlexStartLatestMinute ?? anchor;
        return Math.Clamp(anchor, earliest, latest);
    }

    /// <summary>End ≤ start crosses midnight (spec §4).</summary>
    private static int WrappedLength(int startMinute, int endMinute) =>
        endMinute > startMinute ? endMinute - startMinute : endMinute - startMinute + AttendanceLimits.MaxMinutesPerDay;

    private static TimeOnly ToTime(int minuteOfDay) =>
        TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minuteOfDay % AttendanceLimits.MaxMinutesPerDay));
}
