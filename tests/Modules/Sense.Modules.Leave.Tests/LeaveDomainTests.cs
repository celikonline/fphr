using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Legislation;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Leave.Tests;

public sealed class LeaveDayCounterTests
{
    [Fact]
    public void WorkingDayType_ExcludesWeekendAndHoliday()
    {
        // Mon 2026-10-26 .. Mon 2026-11-02; 29 Oct (Thu) is Cumhuriyet Bayramı.
        var holidays = new Dictionary<DateOnly, string> { [new DateOnly(2026, 10, 29)] = "Cumhuriyet Bayramı" };

        var result = LeaveDayCounter.Count(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 2), LeaveDayCounter.MondayToFriday, holidays, countsNonWorkingDays: false);

        result.Days.ShouldBe(5m);
        result.WeeklyRestDays.ShouldBe(2);
        result.PublicHolidayDays.ShouldBe(1);
        result.Breakdown.Single(d => d.Date == new DateOnly(2026, 10, 29)).HolidayName.ShouldBe("Cumhuriyet Bayramı");
    }

    [Fact]
    public void CalendarDayType_CountsEveryDay()
    {
        var holidays = new Dictionary<DateOnly, string> { [new DateOnly(2026, 10, 29)] = "Cumhuriyet Bayramı" };

        var result = LeaveDayCounter.Count(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 4), LeaveDayCounter.MondayToFriday, holidays, countsNonWorkingDays: true);

        result.Days.ShouldBe(10m);
        result.WeeklyRestDays.ShouldBe(0);
    }
}

public sealed class LeaveTypeTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void IsSensitive_ForcesShowTypeNameOnCalendarOff_OnCreateAndUpdate()
    {
        var set = LeaveRuleSet.Create(TenantId, "Set").Value;
        var created = set.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "Gebelik kontrol", null, true, LeaveLimitType.Unlimited,
            ShowTypeNameOnCalendar: true, IsSensitive: true));

        created.IsSuccess.ShouldBeTrue();
        created.Value.ShowTypeNameOnCalendar.ShouldBeFalse();

        set.UpdateType(created.Value.Id, created.Value.ToSettings() with { IsSensitive = false, ShowTypeNameOnCalendar = true }).IsSuccess.ShouldBeTrue();
        created.Value.ShowTypeNameOnCalendar.ShouldBeTrue();

        set.UpdateType(created.Value.Id, created.Value.ToSettings() with { IsSensitive = true }).IsSuccess.ShouldBeTrue();
        created.Value.ShowTypeNameOnCalendar.ShouldBeFalse();
    }

    [Fact]
    public void Validation_RejectsInvalidWizardCombinations()
    {
        var set = LeaveRuleSet.Create(TenantId, "Set").Value;

        set.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "X", null, true, LeaveLimitType.PerRequest)).Error.Code
            .ShouldBe(LeaveDomainErrors.TypePerRequestMaxDaysInvalid);
        set.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "X", null, true, LeaveLimitType.PerYear, AnnualDays: 0m)).Error.Code
            .ShouldBe(LeaveDomainErrors.TypeAnnualDaysInvalid);
        set.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "X", null, true, LeaveLimitType.PerYear, AnnualDays: 10m,
            CarryOverMode: CarryOverMode.Limited)).Error.Code.ShouldBe(LeaveDomainErrors.TypeCarryOverLimitInvalid);
        set.AddType(new LeaveTypeSettings(LeaveTypeCode.Custom, "X", null, true, LeaveLimitType.PerYear, AnnualDays: 10m,
            SeniorityTiers: [new LeaveSeniorityTier(5, SeniorityComparison.GreaterThan, 6m)])).Error.Code.ShouldBe(LeaveDomainErrors.TypeSeniorityTiersOnlyAnnual);
    }

    [Fact]
    public void Clone_DeepCopiesTypesAndTiers()
    {
        var seed = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(TenantId, StatutoryLeaveDurations.Turkey2026);

        var clone = seed.Clone("İngiltere izin kuralı");

        clone.IsSuccess.ShouldBeTrue();
        clone.Value.Id.ShouldNotBe(seed.Id);
        clone.Value.IsDefault.ShouldBeFalse();
        clone.Value.IsSystemSeed.ShouldBeFalse();
        clone.Value.Types.Count.ShouldBe(seed.Types.Count);
        clone.Value.Types.ShouldAllBe(t => t.RuleSetId == clone.Value.Id);
        clone.Value.Types.Select(t => t.Id).Intersect(seed.Types.Select(t => t.Id)).ShouldBeEmpty();
        var annual = clone.Value.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        annual.SeniorityTiers.Count.ShouldBe(2);
        annual.SeniorityTiers.ShouldNotBeSameAs(seed.Types.Single(t => t.Code == LeaveTypeCode.Annual).SeniorityTiers);
    }

    [Fact]
    public void Seed_ContainsStatutoryTypesAndPassiveTemplates()
    {
        var seed = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(TenantId, StatutoryLeaveDurations.Turkey2026);

        seed.IsDefault.ShouldBeTrue();
        seed.Types.Single(t => t.Code == LeaveTypeCode.Paternity).PerRequestMaxDays.ShouldBe(10m);
        seed.Types.Single(t => t.Code == LeaveTypeCode.Paternity).CountsNonWorkingDays.ShouldBeTrue();
        seed.Types.Single(t => t.Code == LeaveTypeCode.Maternity).PerRequestMaxDays.ShouldBe(168m);
        seed.Types.Single(t => t.Code == LeaveTypeCode.PaidMilitaryService).PerRequestMaxDays.ShouldBe(21m);
        seed.Types.Single(t => t.Code == LeaveTypeCode.FosterCare).IsPaid.ShouldBeFalse();
        seed.Types.Single(t => t.Code == LeaveTypeCode.Sick).ShowTypeNameOnCalendar.ShouldBeFalse();
        var remote = seed.Types.Single(t => t.Code == LeaveTypeCode.RemoteWork);
        remote.IsDisabled.ShouldBeTrue();
        remote.ShowTypeNameOnCalendar.ShouldBeTrue();
        remote.HasBalance.ShouldBeTrue();
    }
}

public sealed class LeaveBalanceEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static LeaveType AnnualType(CarryOverMode carryOver = CarryOverMode.Unlimited, decimal? carryOverMax = null, decimal annualDays = 14m)
    {
        var set = LeaveRuleSet.Create(TenantId, "Set").Value;
        var settings = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).First(t => t.Code == LeaveTypeCode.Annual)
            with { CarryOverMode = carryOver, CarryOverMaxDays = carryOverMax, AnnualDays = annualDays };
        return set.AddType(settings).Value;
    }

    [Theory]
    [InlineData(1, 14)]
    [InlineData(5, 14)]
    [InlineData(6, 20)]
    [InlineData(14, 20)]
    [InlineData(15, 26)]
    [InlineData(20, 26)]
    public void SeniorityTiers_FollowMd53(int anniversary, int expectedDays)
    {
        var start = new DateOnly(2000, 3, 1);

        LeaveBalanceEngine.YearlyEntitlement(AnnualType(), start, start.AddYears(anniversary), null, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe((decimal)expectedDays);
    }

    [Fact]
    public void AnnualType_StatutoryFloorAndAgeException_ApplyEvenWhenConfiguredLower()
    {
        var start = new DateOnly(2020, 1, 1);
        var type = AnnualType(annualDays: 10m);
        var accrualDate = start.AddYears(1);

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, birthDate: new DateOnly(1990, 1, 1), StatutoryAnnualLeave.Turkey2026).ShouldBe(14m);
        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, birthDate: new DateOnly(2003, 6, 1), StatutoryAnnualLeave.Turkey2026).ShouldBe(20m);
        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, birthDate: new DateOnly(1970, 1, 1), StatutoryAnnualLeave.Turkey2026).ShouldBe(20m);
    }

    [Fact]
    public void CarryOver_Modes_IncludingNegativeBalance()
    {
        LeaveBalanceEngine.CarryOverAmount(AnnualType(CarryOverMode.Unlimited), -3m).ShouldBe(-3m);
        LeaveBalanceEngine.CarryOverAmount(AnnualType(CarryOverMode.Limited, 5m), 12m).ShouldBe(5m);
        LeaveBalanceEngine.CarryOverAmount(AnnualType(CarryOverMode.Limited, 5m), -2m).ShouldBe(-2m);
        LeaveBalanceEngine.CarryOverAmount(AnnualType(CarryOverMode.Reset), 12m).ShouldBe(0m);
    }

    [Fact]
    public void Regenerate_CarriesNegativeBalanceIntoNextPeriod()
    {
        var type = AnnualType();
        var start = new DateOnly(2023, 3, 1);
        IReadOnlyCollection<LeaveLedgerEntry> usage =
        [
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, type.Id, 17m, new DateOnly(2024, 6, 3), LeaveLedgerSource.Request, null),
        ];

        var generated = LeaveBalanceEngine.Regenerate(type, TenantId, EmployeeId, start, null, new DateOnly(2025, 9, 15), usage, StatutoryAnnualLeave.Turkey2026);

        generated.Where(e => e.Type == LeaveLedgerEntryType.Accrual).Select(e => (e.EffectiveDate, e.Amount)).ToList()
            .ShouldBe(new[] { (new DateOnly(2024, 3, 1), 14m), (new DateOnly(2025, 3, 1), 14m) });
        generated.Single(e => e.Type == LeaveLedgerEntryType.CarryOver).ShouldSatisfyAllConditions(
            e => e.EffectiveDate.ShouldBe(new DateOnly(2025, 3, 1)),
            e => e.Amount.ShouldBe(-3m));

        var balance = LeaveBalanceEngine.Project(type, start, new DateOnly(2025, 9, 15), [.. usage, .. generated]);
        balance.CarriedOverIntoPeriod.ShouldBe(-3m);
        balance.CurrentBalance.ShouldBe(11m);
        balance.TotalAccrued.ShouldBe(28m);
        balance.TotalUsed.ShouldBe(17m);
    }

    [Fact]
    public void Project_CountsUsageByStartDate()
    {
        var type = AnnualType(CarryOverMode.Reset);
        var start = new DateOnly(2020, 1, 10);
        var today = new DateOnly(2026, 9, 15);
        var periodStart = new DateOnly(2026, 1, 10);
        IReadOnlyCollection<LeaveLedgerEntry> entries =
        [
            LeaveLedgerEntry.Accrual(TenantId, EmployeeId, type.Id, 20m, periodStart),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, type.Id, 5m, new DateOnly(2026, 9, 14), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, type.Id, 3m, new DateOnly(2026, 9, 15), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, type.Id, 4m, new DateOnly(2026, 12, 1), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.Usage(TenantId, EmployeeId, type.Id, 2m, new DateOnly(2027, 2, 1), LeaveLedgerSource.Request, null),
            LeaveLedgerEntry.Adjustment(TenantId, EmployeeId, type.Id, 1m, new DateOnly(2026, 2, 1), "Denkleştirme").Value,
        ];

        var balance = LeaveBalanceEngine.Project(type, start, today, entries);

        balance.CurrentPeriodFrom.ShouldBe(periodStart);
        balance.CurrentPeriodTo.ShouldBe(new DateOnly(2027, 1, 9));
        balance.NextAccrualDate.ShouldBe(new DateOnly(2027, 1, 10));
        balance.TotalUsed.ShouldBe(8m);
        balance.CurrentPeriodUsed.ShouldBe(8m);
        balance.FutureApprovedInPeriod.ShouldBe(4m);
        balance.NextPeriodApproved.ShouldBe(2m);
        balance.CurrentBalance.ShouldBe(13m);
        balance.AvailableBalance.ShouldBe(9m);
    }

    [Fact]
    public void CalendarYearStartOfPeriod_ProratesFirstPartialYear()
    {
        var set = LeaveRuleSet.Create(TenantId, "Set").Value;
        var remote = set.AddType(LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).Single(t => t.Code == LeaveTypeCode.RemoteWork)).Value;

        var generated = LeaveBalanceEngine.Regenerate(remote, TenantId, EmployeeId, new DateOnly(2026, 7, 2), null, new DateOnly(2027, 1, 5), [], null);

        generated.Where(e => e.Type == LeaveLedgerEntryType.Accrual).Select(e => (e.EffectiveDate, e.Amount)).ToList()
            .ShouldBe(new[] { (new DateOnly(2026, 7, 2), decimal.Round(40m * 183 / 365, 4)), (new DateOnly(2027, 1, 1), 40m) });
        generated.ShouldNotContain(e => e.Type == LeaveLedgerEntryType.CarryOver);
    }
}

public sealed class LegislationParameterTests
{
    private static IReadOnlyList<LegislationParameterValue> SeedValues() =>
        LegislationParameterSeed.All
            .Select(p => new LegislationParameterValue(p.Key, p.Country, p.DecimalValue, p.ValueJson, p.EffectiveFrom, p.EffectiveTo, p.Source))
            .ToList();

    [Fact]
    public void Paternity_Is5DaysBefore7578_And10DaysFrom20260501()
    {
        var values = SeedValues();

        values.Resolve(LegislationParameterKeys.PaternityDays, new DateOnly(2026, 4, 30))!.DecimalValue.ShouldBe(5m);
        values.Resolve(LegislationParameterKeys.PaternityDays, new DateOnly(2026, 5, 1))!.DecimalValue.ShouldBe(10m);
        values.Resolve(LegislationParameterKeys.MaternityPostBirthWeeks, new DateOnly(2026, 4, 30))!.DecimalValue.ShouldBe(8m);
        values.Resolve(LegislationParameterKeys.MaternityPostBirthWeeks, new DateOnly(2026, 5, 1))!.DecimalValue.ShouldBe(16m);
        values.Resolve(LegislationParameterKeys.FosterCareDays, new DateOnly(2026, 4, 30)).ShouldBeNull();
    }

    [Fact]
    public void SeedRanges_DoNotOverlap_AndEveryRowCitesASource()
    {
        foreach (var group in LegislationParameterSeed.All.GroupBy(p => (p.Country, p.Key)))
        {
            var ordered = group.OrderBy(p => p.EffectiveFrom).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                ordered[i - 1].EffectiveTo.ShouldNotBeNull();
                ordered[i - 1].EffectiveTo!.Value.ShouldBeLessThan(ordered[i].EffectiveFrom);
            }
        }

        LegislationParameterSeed.All.ShouldAllBe(p => p.Source.Length > 0);
        LegislationParameterSeed.All.Select(p => p.Key).Distinct()
            .ShouldBeSubsetOf(typeof(LegislationParameterKeys).GetFields().Select(f => (string)f.GetRawConstantValue()!));
    }

    [Fact]
    public void StatutoryResolver_SeedsRuleSetWithDurationsOfTheDate()
    {
        var before = StatutoryLeaveResolver.Resolve(SeedValues(), new DateOnly(2026, 4, 30));
        var after = StatutoryLeaveResolver.Resolve(SeedValues(), new DateOnly(2026, 5, 1));

        before.PaternityDays.ShouldBe(5m);
        before.MaternityPostBirthWeeks.ShouldBe(8m);
        after.PaternityDays.ShouldBe(10m);
        after.MaternityPostBirthWeeks.ShouldBe(16m);
        after.Annual.ShouldBe(StatutoryAnnualLeave.Turkey2026);
    }
}
