using Sense.Modules.Attendance.Application.ShiftDefinitions;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;
using static Sense.Modules.Attendance.Tests.ShiftDefinitions.ShiftDefinitionFixtures;

namespace Sense.Modules.Attendance.Tests.ShiftDefinitions;

/// <summary>Spec §5.2: vardiya tanımı kuralları (tolerans, fazla mesai eşikleri, bonus) ve K1 esnek/dinamik plan çözümü.</summary>
public sealed class ShiftRuleTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);
    private static readonly TimeOnly NineAm = new(9, 0);
    private static readonly TimeOnly SixPm = new(18, 0);

    private static ShiftRules Rules(
        int lateTolerance = 0, int earlyLeaveTolerance = 0, int? earlyMin = null, int? earlyMax = null, int? lateMin = null, int? lateMax = null,
        int bonus = 0) =>
        new(lateTolerance, earlyLeaveTolerance, earlyMin, earlyMax, lateMin, lateMax, bonus);

    private static AttendanceDay RecordDay(TimeOnly checkIn, TimeOnly checkOut, ShiftRules rules) =>
        AttendanceDay.Record(
            Tenant, Guid.NewGuid(), Day, checkIn, checkOut, NineAm, SixPm, 480, 60, AttendanceDaySource.Manual,
            shiftLimits: ShiftLimits.None with { Rules = rules });

    private static int Overtime(TimeOnly checkIn, TimeOnly checkOut, ShiftRules rules) =>
        DailyWorkCalculator.Calculate(new DailyWorkInput(checkIn, checkOut, false, 0, 480, ShiftLimits.None with { Rules = rules }, NineAm, SixPm))
            .OvertimeMinutes;

    [Fact]
    public void Late_arrival_respects_tolerance()
    {
        var rules = Rules(lateTolerance: 10);

        RecordDay(new TimeOnly(9, 10), SixPm, rules).Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeFalse();
        RecordDay(new TimeOnly(9, 11), SixPm, rules).Anomalies.HasFlag(AttendanceAnomaly.LateArrival).ShouldBeTrue();
        DailyWorkCalculator.IsLateArrival(new TimeOnly(9, 10), NineAm, rules).ShouldBeFalse();
        DailyWorkCalculator.IsLateArrival(new TimeOnly(9, 11), NineAm, rules).ShouldBeTrue();
    }

    [Fact]
    public void Early_leave_respects_tolerance()
    {
        var rules = Rules(earlyLeaveTolerance: 5);

        RecordDay(NineAm, new TimeOnly(17, 55), rules).Anomalies.HasFlag(AttendanceAnomaly.EarlyLeave).ShouldBeFalse();
        RecordDay(NineAm, new TimeOnly(17, 54), rules).Anomalies.HasFlag(AttendanceAnomaly.EarlyLeave).ShouldBeTrue();
    }

    [Fact]
    public void Without_rules_late_and_early_flags_keep_exact_comparison()
    {
        DailyWorkCalculator.IsLateArrival(new TimeOnly(9, 1), NineAm, null).ShouldBeTrue();
        DailyWorkCalculator.IsLateArrival(NineAm, NineAm, null).ShouldBeFalse();
        DailyWorkCalculator.IsEarlyLeave(new TimeOnly(17, 59), SixPm, null).ShouldBeTrue();
        DailyWorkCalculator.IsEarlyLeave(SixPm, SixPm, null).ShouldBeFalse();
        DailyWorkCalculator.IsLateArrival(null, NineAm, null).ShouldBeFalse();
    }

    [Fact]
    public void Overtime_ignores_below_min_and_caps_at_max()
    {
        var rules = Rules(lateMin: 15, lateMax: 60);

        Overtime(NineAm, new TimeOnly(18, 10), rules).ShouldBe(0);
        Overtime(NineAm, new TimeOnly(18, 30), rules).ShouldBe(30);
        Overtime(NineAm, new TimeOnly(20, 0), rules).ShouldBe(60);
    }

    [Fact]
    public void Overtime_counts_early_arrival_and_late_departure_sides_separately()
    {
        var rules = Rules(earlyMin: 30, earlyMax: 45, lateMin: 15, lateMax: 60);

        Overtime(new TimeOnly(8, 40), new TimeOnly(18, 30), rules).ShouldBe(30);  // erken 20 < 30 → 0; geç 30
        Overtime(new TimeOnly(7, 30), new TimeOnly(18, 30), rules).ShouldBe(75);  // erken 90 → 45; geç 30
    }

    [Fact]
    public void Without_rules_overtime_stays_worked_minus_planned()
    {
        var result = DailyWorkCalculator.Calculate(new DailyWorkInput(NineAm, new TimeOnly(18, 10), false, 0, 480, ShiftLimits.None));

        result.OvertimeMinutes.ShouldBe(10);
    }

    [Fact]
    public void Bonus_minutes_are_added_to_net_worked_minutes_but_not_overtime()
    {
        var withBonus = DailyWorkCalculator.Calculate(
            new DailyWorkInput(NineAm, SixPm, false, 0, 480, ShiftLimits.None with { Rules = Rules(bonus: 20) }, NineAm, SixPm));

        withBonus.NetWorkedMinutes.ShouldBe(480 + 20);
        withBonus.OvertimeMinutes.ShouldBe(0);
    }

    [Fact]
    public void Flexible_planned_end_is_actual_start_plus_duration()
    {
        var flexible = Flexible(480, Minute(7), Minute(10));

        var plan = ShiftRuleEvaluator.Plan(flexible, new TimeOnly(8, 20));

        plan.Start.ShouldBe(new TimeOnly(8, 20));
        plan.End.ShouldBe(new TimeOnly(16, 20));
        plan.PlannedMinutes.ShouldBe(480);
        plan.ShiftDefinitionId.ShouldBe(flexible.Id);
    }

    [Fact]
    public void Flexible_start_outside_window_is_clamped_to_window()
    {
        var flexible = Flexible(480, Minute(7), Minute(10));

        ShiftRuleEvaluator.Plan(flexible, new TimeOnly(10, 30)).Start.ShouldBe(new TimeOnly(10, 0));
        ShiftRuleEvaluator.Plan(flexible, new TimeOnly(6, 15)).Start.ShouldBe(new TimeOnly(7, 0));
    }

    [Fact]
    public void Fixed_plan_takes_break_minutes_from_break_list_and_carries_rules()
    {
        var definition = ShiftDefinition.Create(
            Tenant,
            FixedValues("SAB", Minute(9), Minute(18)) with
            {
                Breaks = [ShiftBreak.Fixed(Minute(12), 45), ShiftBreak.Flexible(15)],
                LateToleranceMinutes = 10,
                BonusMinutes = 5,
            }).Value;

        var plan = ShiftRuleEvaluator.Plan(definition, firstIn: null);

        plan.Start.ShouldBe(NineAm);
        plan.End.ShouldBe(SixPm);
        plan.BreakMinutes.ShouldBe(60);
        plan.PlannedMinutes.ShouldBe(480);
        plan.Rules.ShouldNotBeNull().LateToleranceMinutes.ShouldBe(10);
        plan.Rules!.Value.BonusMinutes.ShouldBe(5);
    }

    [Fact]
    public void Night_fixed_plan_wraps_past_midnight()
    {
        var plan = ShiftRuleEvaluator.Plan(Fixed("GEC", Minute(22), Minute(6)), firstIn: null);

        plan.PlannedMinutes.ShouldBe(480);
    }

    [Fact]
    public void Dynamic_picks_definition_with_closest_start_to_first_in()
    {
        var morning = Dynamic("D06", Minute(6), Minute(14));
        var afternoon = Dynamic("D14", Minute(14), Minute(22));
        var night = Dynamic("D22", Minute(22), Minute(6));

        var match = DynamicShiftMatcher.Match([morning, afternoon, night], new TimeOnly(13, 40));

        match.Definition.ShouldBe(afternoon);
        match.IsAmbiguous.ShouldBeFalse();
    }

    [Fact]
    public void Dynamic_distance_wraps_past_midnight_and_skips_non_dynamic_or_inactive()
    {
        var morning = Dynamic("D06", Minute(6), Minute(14));
        var night = Dynamic("D22", Minute(22), Minute(6));
        var fixedMidnight = Fixed("F00", 0, Minute(8));
        var inactiveMidnight = Dynamic("D00", 0, Minute(8));
        inactiveMidnight.Deactivate();

        DynamicShiftMatcher.Match([morning, night, fixedMidnight, inactiveMidnight], new TimeOnly(0, 30)).Definition.ShouldBe(night);
    }

    [Fact]
    public void Dynamic_tie_is_ambiguous()
    {
        var match = DynamicShiftMatcher.Match([Dynamic("D06", Minute(6), Minute(14)), Dynamic("D10", Minute(10), Minute(18))], new TimeOnly(8, 0));

        match.Definition.ShouldBeNull();
        match.IsAmbiguous.ShouldBeTrue();
    }
}
