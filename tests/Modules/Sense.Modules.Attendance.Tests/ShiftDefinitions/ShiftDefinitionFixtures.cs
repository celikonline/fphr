using Sense.Modules.Attendance.Domain.Shifts;

namespace Sense.Modules.Attendance.Tests.ShiftDefinitions;

/// <summary>Vardiya tanımı kurucuları (Z0.2 testleri): dakikalar gün başından.</summary>
internal static class ShiftDefinitionFixtures
{
    public static readonly Guid Tenant = Guid.NewGuid();

    public static ShiftDefinitionValues FixedValues(string code, int startMinute, int endMinute) => new(
        code, code, "#336699", ShiftType.Fixed, startMinute, endMinute, StartsPreviousDay: false, DurationMinutes: null,
        FlexStartEarliestMinute: null, FlexStartLatestMinute: null, [], LateToleranceMinutes: 0, EarlyLeaveToleranceMinutes: 0,
        EarlyArrivalOvertimeMinMinutes: null, EarlyArrivalOvertimeMaxMinutes: null, LateDepartureOvertimeMinMinutes: null,
        LateDepartureOvertimeMaxMinutes: null, BonusMinutes: 0);

    public static ShiftDefinition Fixed(string code, int startMinute, int endMinute) =>
        ShiftDefinition.Create(Tenant, FixedValues(code, startMinute, endMinute)).Value;

    public static ShiftDefinition Dynamic(string code, int startMinute, int endMinute) =>
        ShiftDefinition.Create(Tenant, FixedValues(code, startMinute, endMinute) with { Type = ShiftType.Dynamic }).Value;

    public static ShiftDefinition Flexible(int durationMinutes, int earliestMinute, int latestMinute) =>
        ShiftDefinition.Create(
            Tenant,
            FixedValues("ESN", earliestMinute, earliestMinute) with
            {
                Type = ShiftType.Flexible,
                DurationMinutes = durationMinutes,
                FlexStartEarliestMinute = earliestMinute,
                FlexStartLatestMinute = latestMinute,
            }).Value;

    public static int Minute(int hour, int minute = 0) => (hour * 60) + minute;
}
