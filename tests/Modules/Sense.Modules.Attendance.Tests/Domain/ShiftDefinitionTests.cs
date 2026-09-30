using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Domain;

/// <summary>Kural setli vardiya tanımı (Z0.2, spec §4) ve vardiya atamasının tanım/gün türü bağı.</summary>
public sealed class ShiftDefinitionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ShiftDefinitionValues Fixed(int start = 9 * 60, int end = 18 * 60) => new(
        "SAB", "Sabah", "#1a2b3c", ShiftType.Fixed, start, end, StartsPreviousDay: false, DurationMinutes: null, FlexStartEarliestMinute: null,
        FlexStartLatestMinute: null, [ShiftBreak.Fixed(12 * 60, 60)], LateToleranceMinutes: 5, EarlyLeaveToleranceMinutes: 5,
        EarlyArrivalOvertimeMinMinutes: null, EarlyArrivalOvertimeMaxMinutes: null, LateDepartureOvertimeMinMinutes: 30, LateDepartureOvertimeMaxMinutes: 180,
        BonusMinutes: 0);

    [Fact]
    public void Create_normalizes_and_keeps_rules()
    {
        var definition = ShiftDefinition.Create(Tenant, Fixed() with { Code = "  SAB ", Name = " Sabah " }).Value;

        definition.Code.ShouldBe("SAB");
        definition.Name.ShouldBe("Sabah");
        definition.ColorHex.ShouldBe("#1A2B3C");
        definition.Type.ShouldBe(ShiftType.Fixed);
        definition.StartMinute.ShouldBe(540);
        definition.EndMinute.ShouldBe(1080);
        definition.Breaks.ShouldHaveSingleItem().DurationMinutes.ShouldBe(60);
        definition.LateDepartureOvertimeMaxMinutes.ShouldBe(180);
        definition.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Night_shift_with_end_before_start_is_valid()
    {
        ShiftDefinition.Create(Tenant, Fixed(22 * 60, 6 * 60)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Flexible_shift_requires_duration_and_ordered_window()
    {
        var flexible = Fixed() with { Type = ShiftType.Flexible, DurationMinutes = 540, FlexStartEarliestMinute = 7 * 60, FlexStartLatestMinute = 10 * 60 };
        ShiftDefinition.Create(Tenant, flexible).IsSuccess.ShouldBeTrue();

        ShiftDefinition.Create(Tenant, flexible with { DurationMinutes = null }).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionFlexibleWindowInvalid);
        ShiftDefinition.Create(Tenant, flexible with { FlexStartEarliestMinute = 11 * 60 }).Error.Code
            .ShouldBe(AttendanceDomainErrors.ShiftDefinitionFlexibleWindowInvalid);
    }

    [Fact]
    public void Fixed_shift_rejects_flexible_fields()
    {
        ShiftDefinition.Create(Tenant, Fixed() with { DurationMinutes = 480 }).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionFlexibleWindowInvalid);
    }

    [Theory]
    [InlineData("", "Sabah", "#112233", AttendanceDomainErrors.ShiftDefinitionCodeRequired)]
    [InlineData("SAB", " ", "#112233", AttendanceDomainErrors.ShiftDefinitionNameRequired)]
    [InlineData("SAB", "Sabah", "112233", AttendanceDomainErrors.ShiftDefinitionColorInvalid)]
    [InlineData("SAB", "Sabah", "#11223G", AttendanceDomainErrors.ShiftDefinitionColorInvalid)]
    public void Create_rejects_invalid_identity_fields(string code, string name, string color, string expected)
    {
        ShiftDefinition.Create(Tenant, Fixed() with { Code = code, Name = name, ColorHex = color }).Error.Code.ShouldBe(expected);
    }

    [Theory]
    [InlineData(540, 540)]
    [InlineData(-1, 600)]
    [InlineData(540, 1440)]
    public void Create_rejects_invalid_time_range(int start, int end)
    {
        ShiftDefinition.Create(Tenant, Fixed(start, end)).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionTimeRangeInvalid);
    }

    [Fact]
    public void Create_rejects_negative_tolerance_and_inverted_overtime_range()
    {
        ShiftDefinition.Create(Tenant, Fixed() with { LateToleranceMinutes = -1 }).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionToleranceInvalid);
        ShiftDefinition.Create(Tenant, Fixed() with { EarlyArrivalOvertimeMinMinutes = 60, EarlyArrivalOvertimeMaxMinutes = 30 }).Error.Code
            .ShouldBe(AttendanceDomainErrors.ShiftDefinitionOvertimeRangeInvalid);
    }

    [Fact]
    public void Create_rejects_invalid_breaks()
    {
        ShiftDefinition.Create(Tenant, Fixed() with { Breaks = [ShiftBreak.Flexible(0)] }).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionBreakInvalid);
        ShiftDefinition.Create(Tenant, Fixed() with { Breaks = [ShiftBreak.Fixed(1500, 30)] }).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDefinitionBreakInvalid);
    }

    [Fact]
    public void Update_replaces_breaks_and_failed_update_keeps_previous_state()
    {
        var definition = ShiftDefinition.Create(Tenant, Fixed()).Value;

        definition.Update(Fixed() with { Breaks = [ShiftBreak.Flexible(30), ShiftBreak.Fixed(15 * 60, 15)] }).IsSuccess.ShouldBeTrue();
        definition.Breaks.Count.ShouldBe(2);

        definition.Update(Fixed() with { Name = "" }).IsFailure.ShouldBeTrue();
        definition.Name.ShouldBe("Sabah");
        definition.Breaks.Count.ShouldBe(2);
    }

    [Fact]
    public void Assignment_defaults_to_work_day_without_definition_and_can_be_bound()
    {
        var assignment = ShiftAssignment.Create(Tenant, Guid.NewGuid(), new DateOnly(2026, 9, 1), new TimeOnly(9, 0), new TimeOnly(18, 0), 60, false, null).Value;
        assignment.DayType.ShouldBe(ShiftDayType.Work);
        assignment.ShiftDefinitionId.ShouldBeNull();

        var definitionId = Guid.NewGuid();
        assignment.SetPlan(definitionId, ShiftDayType.WeeklyRest).IsSuccess.ShouldBeTrue();

        assignment.ShiftDefinitionId.ShouldBe(definitionId);
        assignment.DayType.ShouldBe(ShiftDayType.WeeklyRest);
        assignment.SetPlan(null, (ShiftDayType)99).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftDayTypeInvalid);
    }
}
