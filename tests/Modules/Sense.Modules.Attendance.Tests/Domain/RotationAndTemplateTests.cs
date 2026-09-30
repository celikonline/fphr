using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.Rotations;
using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Domain;

/// <summary>Rotasyon kalıbı/ataması (Z3.3) ve vardiya planı şablonu (Z3.2).</summary>
public sealed class RotationAndTemplateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Day = Guid.NewGuid();
    private static readonly Guid Night = Guid.NewGuid();

    [Fact]
    public void Pattern_keeps_steps_ordered_by_offset()
    {
        var pattern = RotationPattern.Create(
            Tenant, " 2X12 ", "İki vardiya", RotationPreset.TwoByTwelve, 4,
            [RotationStep.Of(2, null, ShiftDayType.RestDay), RotationStep.Of(0, Day, ShiftDayType.Work), RotationStep.Of(1, Night, ShiftDayType.Work)]).Value;

        pattern.Code.ShouldBe("2X12");
        pattern.IsActive.ShouldBeTrue();
        pattern.Steps.Select(s => s.DayOffset).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Pattern_rejects_bad_cycle_and_bad_steps()
    {
        RotationPattern.Create(Tenant, "R", "R", RotationPreset.Custom, 0, [RotationStep.Of(0, Day, ShiftDayType.Work)]).Error.Code
            .ShouldBe(AttendanceDomainErrors.RotationCycleInvalid);
        RotationPattern.Create(Tenant, "R", "R", RotationPreset.Custom, 2, []).Error.Code.ShouldBe(AttendanceDomainErrors.RotationStepInvalid);
        RotationPattern.Create(Tenant, "R", "R", RotationPreset.Custom, 2, [RotationStep.Of(2, Day, ShiftDayType.Work)]).Error.Code
            .ShouldBe(AttendanceDomainErrors.RotationStepInvalid);
        RotationPattern.Create(Tenant, "R", "R", RotationPreset.Custom, 2, [RotationStep.Of(0, Day, ShiftDayType.Work), RotationStep.Of(0, Night, ShiftDayType.Work)])
            .Error.Code.ShouldBe(AttendanceDomainErrors.RotationStepInvalid);
        RotationPattern.Create(Tenant, " ", "R", RotationPreset.Custom, 2, [RotationStep.Of(0, Day, ShiftDayType.Work)]).Error.Code
            .ShouldBe(AttendanceDomainErrors.RotationPatternCodeRequired);
    }

    [Fact]
    public void Assignment_covers_its_range_and_can_be_ended()
    {
        var assignment = RotationAssignment.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), null, offsetDays: 2).Value;

        assignment.Covers(new DateOnly(2026, 8, 31)).ShouldBeFalse();
        assignment.Covers(new DateOnly(2027, 1, 1)).ShouldBeTrue();

        assignment.End(new DateOnly(2026, 9, 30)).IsSuccess.ShouldBeTrue();
        assignment.Covers(new DateOnly(2026, 10, 1)).ShouldBeFalse();
        assignment.End(new DateOnly(2026, 8, 1)).Error.Code.ShouldBe(AttendanceDomainErrors.RotationAssignmentRangeInvalid);
    }

    [Fact]
    public void Assignment_rejects_end_before_start_and_negative_offset()
    {
        RotationAssignment.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1), 0).Error.Code
            .ShouldBe(AttendanceDomainErrors.RotationAssignmentRangeInvalid);
        RotationAssignment.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 1), null, -1).Error.Code
            .ShouldBe(AttendanceDomainErrors.RotationAssignmentRangeInvalid);
    }

    [Fact]
    public void Template_keeps_entries_and_rejects_duplicates_or_out_of_period()
    {
        var employee = Guid.NewGuid();
        var user = Guid.NewGuid();

        var template = ShiftPlanTemplate.Create(
            Tenant, " Haftalık ", 7, [ShiftPlanTemplateEntry.Of(employee, 0, Day, ShiftDayType.Work), ShiftPlanTemplateEntry.Of(employee, 6, null, ShiftDayType.WeeklyRest)],
            user).Value;

        template.Name.ShouldBe("Haftalık");
        template.Entries.Count.ShouldBe(2);
        template.CreatedByUserId.ShouldBe(user);

        ShiftPlanTemplate.Create(Tenant, "T", 7, [ShiftPlanTemplateEntry.Of(employee, 7, Day, ShiftDayType.Work)], user).Error.Code
            .ShouldBe(AttendanceDomainErrors.ShiftTemplateEntryInvalid);
        ShiftPlanTemplate.Create(
                Tenant, "T", 7, [ShiftPlanTemplateEntry.Of(employee, 1, Day, ShiftDayType.Work), ShiftPlanTemplateEntry.Of(employee, 1, Night, ShiftDayType.Work)], user)
            .Error.Code.ShouldBe(AttendanceDomainErrors.ShiftTemplateEntryInvalid);
        ShiftPlanTemplate.Create(Tenant, "T", 0, [], user).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftTemplatePeriodInvalid);
        ShiftPlanTemplate.Create(Tenant, "", 7, [], user).Error.Code.ShouldBe(AttendanceDomainErrors.ShiftTemplateNameRequired);
    }
}
