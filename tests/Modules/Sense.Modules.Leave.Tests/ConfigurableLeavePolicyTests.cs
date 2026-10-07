using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.RuleSets;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class ConfigurableLeavePolicyTests
{
    [Fact]
    public void Seed_ConfiguresBirthdayAndHourlyDefaults()
    {
        var types = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).ToDictionary(t => t.Code);

        types[LeaveTypeCode.Birthday].UseOnlyOnBirthday.ShouldBeTrue();
        types[LeaveTypeCode.Birthday].Feb29Policy.ShouldBe(LeaveFeb29Policy.UseFebruary28);
        types[LeaveTypeCode.JobSearch].HourlyHalfDayThresholdMinutes.ShouldBe(180);
        types[LeaveTypeCode.Breastfeeding].HourlyHalfDayDays.ShouldBe(0.5m);
    }

    [Fact]
    public void ConfiguredPolicies_RoundTripThroughLeaveTypeSettings()
    {
        var set = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(Guid.NewGuid(), StatutoryLeaveDurations.Turkey2026);
        var hourly = set.Types.Single(t => t.Code == LeaveTypeCode.JobSearch);
        var birthday = set.Types.Single(t => t.Code == LeaveTypeCode.Birthday);

        set.UpdateType(hourly.Id, hourly.ToSettings() with { HourlyHalfDayThresholdMinutes = 240, HourlyHalfDayDays = 0.75m }).IsSuccess.ShouldBeTrue();
        set.UpdateType(birthday.Id, birthday.ToSettings() with { Feb29Policy = LeaveFeb29Policy.UseMarch1 }).IsSuccess.ShouldBeTrue();

        set.Types.Single(t => t.Code == LeaveTypeCode.JobSearch).ToSettings().HourlyHalfDayThresholdMinutes.ShouldBe(240);
        set.Types.Single(t => t.Code == LeaveTypeCode.JobSearch).ToSettings().HourlyHalfDayDays.ShouldBe(0.75m);
        set.Types.Single(t => t.Code == LeaveTypeCode.Birthday).ToSettings().Feb29Policy.ShouldBe(LeaveFeb29Policy.UseMarch1);
    }

    [Fact]
    public void LeaveTypeSettings_RejectsHourlyThresholdOutsideHourlyUnitsAndInvalidBounds()
    {
        var hourly = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).Single(t => t.Code == LeaveTypeCode.JobSearch);

        (hourly with { HourlyHalfDayThresholdMinutes = 0 }).Validate().Error.Code.ShouldBe(LeaveDomainErrors.TypeHourlyThresholdInvalid);
        (hourly with { Unit = LeaveUnit.FullDay }).Validate().Error.Code.ShouldBe(LeaveDomainErrors.TypeHourlyThresholdInvalid);
        (hourly with { HourlyHalfDayDays = 1.1m }).Validate().Error.Code.ShouldBe(LeaveDomainErrors.TypeHourlyHalfDayDaysInvalid);
    }

    [Theory]
    [InlineData(LeaveFeb29Policy.UseFebruary28, 2025, 2, 28, true)]
    [InlineData(LeaveFeb29Policy.UseFebruary28, 2025, 3, 1, false)]
    [InlineData(LeaveFeb29Policy.UseMarch1, 2025, 2, 28, false)]
    [InlineData(LeaveFeb29Policy.UseMarch1, 2025, 3, 1, true)]
    [InlineData(LeaveFeb29Policy.UseFebruary28, 2024, 2, 29, true)]
    [InlineData(LeaveFeb29Policy.UseMarch1, 2024, 2, 29, true)]
    public void BirthdayPolicy_ResolvesLeapDay(LeaveFeb29Policy policy, int year, int month, int day, bool expected)
    {
        LeaveBirthdayPolicy.IsBirthday(new DateOnly(2000, 2, 29), new DateOnly(year, month, day), policy).ShouldBe(expected);
    }

    [Theory]
    [InlineData(179, 180, 0.5, 0.3978)]
    [InlineData(180, 180, 0.5, 0.4)]
    [InlineData(181, 180, 0.5, 0.5)]
    [InlineData(181, 180, 0.75, 0.75)]
    public void HourlyFraction_UsesStrictConfigurableHalfDayThreshold(int minutes, int threshold, decimal halfDayDays, decimal expected)
    {
        var start = new TimeOnly(9, 0);
        var result = LeavePartialDay.HourlyFractionWithThreshold(start, start.AddMinutes(minutes), threshold, halfDayDays);

        result.ShouldBe(expected);
    }
}
