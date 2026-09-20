using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
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

    private static readonly IReadOnlyDictionary<DateOnly, string> NoHolidays = new Dictionary<DateOnly, string>();

    /// <summary>"Pazartesi'den Cuma'ya izin alırsa Cumartesi'yi ekle" (roadmap #23, 11.3.5/11.3.6).</summary>
    [Fact]
    public void SaturdayRule_FullMondayToFridayRequest_AddsTheFollowingSaturdayPastTheRequestedEnd()
    {
        // Mon 2026-10-26 .. Fri 2026-10-30 requested; Saturday 10-31 is not itself in the request.
        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30), LeaveDayCounter.MondayToFriday, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => true);

        result.Days.ShouldBe(6m);
        result.Breakdown.Count.ShouldBe(6, "Saturday is appended even though it falls after the requested end date");
        var saturday = result.Breakdown.Single(d => d.Date == new DateOnly(2026, 10, 31));
        saturday.Kind.ShouldBe(LeaveDayKind.WeeklyRest);
        saturday.Counted.ShouldBeTrue();
    }

    [Fact]
    public void SaturdayRule_WithoutTheFlag_LeavesTheCountUnchanged()
    {
        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30), LeaveDayCounter.MondayToFriday, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => false);

        result.Days.ShouldBe(5m);
        result.Breakdown.Count.ShouldBe(5);
    }

    [Fact]
    public void SaturdayRule_PartialWeekRequest_DoesNotApply()
    {
        // Wed–Fri only: Monday and Tuesday (working days) are not requested, so the week is not "fully" taken.
        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 30), LeaveDayCounter.MondayToFriday, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => true);

        result.Days.ShouldBe(3m);
        result.Breakdown.ShouldNotContain(d => d.Date == new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void SaturdayRule_WhenSaturdayIsAWorkingDay_DoesNotApply()
    {
        // "Cumartesi çalışma" schedules are supported: the rule never fires for a Saturday that is itself scheduled.
        bool AlwaysWorking(DateOnly date) => date.DayOfWeek is not DayOfWeek.Sunday;

        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30), AlwaysWorking, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => true);

        result.Breakdown.ShouldNotContain(d => d.Date == new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void SaturdayRule_WithAMidWeekRestDayOutsideTheRequest_StillAppliesToTheWorkingDays()
    {
        // "Perşembe tatil" style schedule (11.3.5): Monday is off, Tue–Fri working. The employee only requests the
        // working days (Monday needs no leave); the missing Monday must not block the Saturday rule.
        bool TuesdayToFridayOnly(DateOnly date) => date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday;

        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 30), TuesdayToFridayOnly, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => true);

        result.Days.ShouldBe(5m, "4 requested working days + the added Saturday");
        result.Breakdown.Single(d => d.Date == new DateOnly(2026, 10, 31)).Counted.ShouldBeTrue();
    }

    [Fact]
    public void SaturdayRule_WhenAWorkingDayInTheWeekIsNotRequested_DoesNotApply()
    {
        // The Thursday-holiday open point (11.3.5 §Gün hesabı): Thursday is a normal working day per schedule but the
        // employee's continuous request stops on Wednesday - the default is that the rule does not relax for this.
        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 28), LeaveDayCounter.MondayToFriday, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => true);

        result.Breakdown.ShouldNotContain(d => d.Date == new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void SaturdayRule_TwoConsecutiveFullWeeks_AppliesToEachWeekIndependently()
    {
        // Mon 2026-10-26 .. Fri 2026-11-06: two full weeks: the first Saturday (10-31) is already inside the range,
        // the second (11-07) falls right after it - "her takvim haftası ayrı değerlendirilir".
        var result = LeaveDayCounter.Count(
            new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 6), LeaveDayCounter.MondayToFriday, NoHolidays,
            countsNonWorkingDays: false, includeSaturdayWhenFullWeek: _ => true);

        result.Days.ShouldBe(12m, "10 workdays + both Saturdays; the Sunday between them stays excluded");
        result.WeeklyRestDays.ShouldBe(1);
        result.Breakdown.Single(d => d.Date == new DateOnly(2026, 10, 31)).Counted.ShouldBeTrue();
        result.Breakdown.Single(d => d.Date == new DateOnly(2026, 11, 7)).Counted.ShouldBeTrue();
        result.Breakdown.Single(d => d.Date == new DateOnly(2026, 11, 1)).Counted.ShouldBeFalse();
    }
}

/// <summary>Fractional-day calculation for LeaveType.Unit = HalfDay/Hourly (11.3.5 - yarım gün ve saatlik izin).</summary>
public sealed class LeavePartialDayTests
{
    [Fact]
    public void HalfDayFraction_IsAlwaysHalfADay() => LeavePartialDay.HalfDayFraction.ShouldBe(0.5m);

    [Fact]
    public void HourlyFraction_UsesTheDefaultDailyWorkMinutes_SameBasisAsOvertime()
    {
        // 7.5 h/day default (md.63: 45 h / 6 gün); 3 h 45 min = half a day.
        var result = LeavePartialDay.HourlyFraction(new TimeOnly(9, 0), new TimeOnly(12, 45));

        result.ShouldBe(0.5m);
        LeavePartialDay.DefaultDailyWorkMinutes.ShouldBe(450);
    }

    [Fact]
    public void HourlyFraction_RoundsToLedgerPrecision()
    {
        // 2 hours of a 450-minute day = 120/450 = 0.2666...; rounds to 4 decimals like LeaveLedgerEntry.Amount.
        var result = LeavePartialDay.HourlyFraction(new TimeOnly(9, 0), new TimeOnly(11, 0));

        result.ShouldBe(0.2667m);
    }

    [Fact]
    public void HourlyFraction_HonorsACustomDailyWorkMinutes() =>
        LeavePartialDay.HourlyFraction(new TimeOnly(9, 0), new TimeOnly(13, 0), dailyWorkMinutes: 480).ShouldBe(0.5m);

    [Fact]
    public void HoursFraction_MatchesHourlyFractionForTheSameSpan() =>
        // senseik-036 md.5: 4857 md.27 iş arama izni günde en az 2 saat = 120/450 gün kesri.
        LeavePartialDay.HoursFraction(2m).ShouldBe(LeavePartialDay.HourlyFraction(new TimeOnly(9, 0), new TimeOnly(11, 0)));

    [Fact]
    public void HoursFraction_HalfDayAndAHalf_IsExactlyOneFifth() =>
        // senseik-036 md.3: 4857 md.74/son süt izni günde toplam 1,5 saat = 90/450 = 0,2 gün kesri (tam sayı, yuvarlama yok).
        LeavePartialDay.HoursFraction(1.5m).ShouldBe(0.2m);
}

public sealed class SickLeaveAbsenceThresholdTests
{
    [Theory]
    [InlineData(41.9999, false)]
    [InlineData(42, true)]
    [InlineData(60, true)]
    [InlineData(0, false)]
    public void IsReached_ComparesAgainstSixWeeks(decimal usedDays, bool expected) =>
        SickLeaveAbsenceThreshold.IsReached(usedDays).ShouldBe(expected);

    [Fact]
    public void MinimumDays_IsSixWeeks() => SickLeaveAbsenceThreshold.MinimumDays.ShouldBe(42m);
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
        // babalık 10 gün, doğum 24 hafta/168 gün (7578 s.K. referanslı) - senseik-036 kartındaki 5/112/126 değerleri
        // hukuki onay gelene kadar bilinçli UYGULANMADI (bkz. handoff "Ertelenen kalemler"); bu iddialar o kararın kilididir.
        seed.Types.Single(t => t.Code == LeaveTypeCode.Paternity).PerRequestMaxDays.ShouldBe(10m);
        seed.Types.Single(t => t.Code == LeaveTypeCode.Paternity).CountsNonWorkingDays.ShouldBeTrue();
        seed.Types.Single(t => t.Code == LeaveTypeCode.Maternity).PerRequestMaxDays.ShouldBe(168m);
        seed.Types.Single(t => t.Code == LeaveTypeCode.PaidMilitaryService).PerRequestMaxDays.ShouldBe(21m);
        seed.Types.Single(t => t.Code == LeaveTypeCode.FosterCare).IsPaid.ShouldBeFalse();
        seed.Types.Single(t => t.Code == LeaveTypeCode.Sick).ShowTypeNameOnCalendar.ShouldBeFalse();
        seed.Types.Single(t => t.Code == LeaveTypeCode.Sick).IsSensitive.ShouldBeTrue();
        var remote = seed.Types.Single(t => t.Code == LeaveTypeCode.RemoteWork);
        remote.IsDisabled.ShouldBeTrue();
        remote.ShowTypeNameOnCalendar.ShouldBeTrue();
        remote.HasBalance.ShouldBeTrue();

        // senseik-036 md.6 (4857 md.56/3): yıllık izinde asgari blok 10 gün (bkz. AnnualLeaveSplitRuleTests).
        seed.Types.Single(t => t.Code == LeaveTypeCode.Annual).MinConsecutiveDays.ShouldBe(10m);

        // senseik-036 md.5: 4857 md.27 - iş arama izni artık "Limitsiz" değil; günde en az 2 saat (120/450 gün kesri), tek gün.
        var jobSearch = seed.Types.Single(t => t.Code == LeaveTypeCode.JobSearch);
        jobSearch.LimitType.ShouldBe(LeaveLimitType.PerRequest);
        jobSearch.PerRequestMaxDays.ShouldBe(1m);
        jobSearch.MinConsecutiveDays.ShouldBe(0.2667m);

        // senseik-036 md.3: süt izni - günde toplam en fazla 1,5 saat (90/450 = 0,2 gün kesri).
        var breastfeeding = seed.Types.Single(t => t.Code == LeaveTypeCode.Breastfeeding);
        breastfeeding.Unit.ShouldBe(LeaveUnit.Hourly);
        breastfeeding.LimitType.ShouldBe(LeaveLimitType.PerRequest);
        breastfeeding.MaxConsecutiveDays.ShouldBe(0.2m);

        // senseik-036 md.4: engelli/süreğen hastalıklı çocuk izni - yılda 10 gün, PerYear (bakiyeli).
        var disabledChild = seed.Types.Single(t => t.Code == LeaveTypeCode.DisabledChild);
        disabledChild.LimitType.ShouldBe(LeaveLimitType.PerYear);
        disabledChild.AnnualDays.ShouldBe(10m);
        disabledChild.IsSensitive.ShouldBeTrue();
    }
}

public sealed class AnnualLeaveSplitRuleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Theory]
    [InlineData(6, 14, true)] // 14 gün kaldı, 6 gün alınırsa 8 kalır: 10 günlük blok imkansızlaşır
    [InlineData(4, 14, false)] // 10 gün kalır: blok hâlâ sığar
    [InlineData(9, 9, false)] // bakiye zaten bir bloğa yetmiyor: kural devreye girmez (izin sahipsiz kalmaz)
    [InlineData(10, 14, false)] // talep kendisi bir blok
    [InlineData(2, 10, true)] // tam 10 gün kalmışken 2 gün almak bloğu yok eder
    public void WouldForfeitMinimumBlock_OnlyWhenTheRequestStrandsTheLastBlock(int requestDays, int availableDays, bool expected) =>
        AnnualLeaveSplitRule.WouldForfeitMinimumBlock(10m, requestDays, availableDays).ShouldBe(expected);

    [Fact]
    public void HasBookedBlock_TrueOnlyWhenABookedRequestIsAFullBlock()
    {
        AnnualLeaveSplitRule.HasBookedBlock(10m, [2m, 3m]).ShouldBeFalse();
        AnnualLeaveSplitRule.HasBookedBlock(10m, [2m, 10m]).ShouldBeTrue();
        AnnualLeaveSplitRule.HasBookedBlock(10m, []).ShouldBeFalse();
    }

    [Fact]
    public void Applies_OnlyToTheAnnualTypeWithAPositiveMinimum()
    {
        var seed = LeaveRuleSetSeed.CreateTurkishLabourLawRuleSet(TenantId, StatutoryLeaveDurations.Turkey2026);

        AnnualLeaveSplitRule.Applies(seed.Types.Single(t => t.Code == LeaveTypeCode.Annual)).ShouldBeTrue();
        AnnualLeaveSplitRule.Applies(seed.Types.Single(t => t.Code == LeaveTypeCode.JobSearch)).ShouldBeFalse();

        var annual = seed.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        seed.UpdateType(annual.Id, annual.ToSettings() with { MinConsecutiveDays = null }).IsSuccess.ShouldBeTrue();
        AnnualLeaveSplitRule.Applies(annual).ShouldBeFalse();
    }
}

public sealed class LeaveRuleSetSeedSyncTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly IReadOnlyCollection<LeaveTypeSettings> Current = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).ToList();

    /// <summary>A system-seed rule set as provisioned before senseik-036: no süt/engelli çocuk türleri, Annual min boş, iş arama "Limitsiz".</summary>
    private static LeaveRuleSet LegacySeededSet(bool isSystemSeed = true)
    {
        var set = LeaveRuleSet.Create(TenantId, LeaveRuleSetSeed.DefaultRuleSetName, isDefault: true, isSystemSeed: isSystemSeed).Value;
        foreach (var type in Current.Where(t => t.Code is not (LeaveTypeCode.Breastfeeding or LeaveTypeCode.DisabledChild)))
        {
            var legacy = type.Code switch
            {
                LeaveTypeCode.Annual => type with { MinConsecutiveDays = null },
                LeaveTypeCode.JobSearch => type with { LimitType = LeaveLimitType.Unlimited, PerRequestMaxDays = null, MinConsecutiveDays = null },
                _ => type,
            };
            set.AddType(legacy).IsSuccess.ShouldBeTrue();
        }

        return set;
    }

    [Fact]
    public void Apply_AddsMissingTypesAndUpgradesUntouchedLegacyValues()
    {
        var set = LegacySeededSet();

        var changes = LeaveRuleSetSeedSync.Apply(set, Current);

        changes.ShouldBe(4);
        set.Types.ShouldContain(t => t.Code == LeaveTypeCode.Breastfeeding);
        set.Types.Single(t => t.Code == LeaveTypeCode.DisabledChild).AnnualDays.ShouldBe(10m);
        set.Types.Single(t => t.Code == LeaveTypeCode.Annual).MinConsecutiveDays.ShouldBe(10m);
        var jobSearch = set.Types.Single(t => t.Code == LeaveTypeCode.JobSearch);
        jobSearch.LimitType.ShouldBe(LeaveLimitType.PerRequest);
        jobSearch.MinConsecutiveDays.ShouldBe(0.2667m);
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var set = LegacySeededSet();
        LeaveRuleSetSeedSync.Apply(set, Current);

        LeaveRuleSetSeedSync.Apply(set, Current).ShouldBe(0);
        set.Types.Count(t => t.Code == LeaveTypeCode.Breastfeeding).ShouldBe(1);
    }

    [Fact]
    public void Apply_KeepsAdminCustomisedValues()
    {
        var set = LegacySeededSet();
        var annual = set.Types.Single(t => t.Code == LeaveTypeCode.Annual);
        set.UpdateType(annual.Id, annual.ToSettings() with { MinConsecutiveDays = 5m }).IsSuccess.ShouldBeTrue();
        var jobSearch = set.Types.Single(t => t.Code == LeaveTypeCode.JobSearch);
        set.UpdateType(jobSearch.Id, jobSearch.ToSettings() with { MinConsecutiveDays = 0.5m }).IsSuccess.ShouldBeTrue();

        LeaveRuleSetSeedSync.Apply(set, Current);

        set.Types.Single(t => t.Code == LeaveTypeCode.Annual).MinConsecutiveDays.ShouldBe(5m);
        set.Types.Single(t => t.Code == LeaveTypeCode.JobSearch).LimitType.ShouldBe(LeaveLimitType.Unlimited);
    }

    [Fact]
    public void Apply_NeverTouchesNonSeedRuleSets()
    {
        var custom = LegacySeededSet(isSystemSeed: false);

        LeaveRuleSetSeedSync.Apply(custom, Current).ShouldBe(0);
        custom.Types.ShouldNotContain(t => t.Code == LeaveTypeCode.Breastfeeding);
    }
}

public sealed class LeaveBalanceEngineTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static LeaveType AnnualType(
        CarryOverMode carryOver = CarryOverMode.Unlimited, decimal? carryOverMax = null, decimal annualDays = 14m,
        AccrualFrequency accrualFrequency = AccrualFrequency.Yearly, AccrualTiming accrualTiming = AccrualTiming.EndOfPeriod)
    {
        var set = LeaveRuleSet.Create(TenantId, "Set").Value;
        var settings = LeaveRuleSetSeed.Types(StatutoryLeaveDurations.Turkey2026).First(t => t.Code == LeaveTypeCode.Annual)
            with { CarryOverMode = carryOver, CarryOverMaxDays = carryOverMax, AnnualDays = annualDays, AccrualFrequency = accrualFrequency, AccrualTiming = accrualTiming };
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

    /// <summary>ADR-0024 / Bulgu 1: yaş istisnası tabanı (md.53/IV) 1 yıllık kıdem şartına bağlıdır — kıdem
    /// dolmadan (years=0) bir Monthly/StartOfPeriod yapılandırmada yaş istisnasının 20 günlük tabanı
    /// uygulanmamalı; sonuç türün normal (kademesiz) tabanında kalmalı, 20'ye yükselmemeli.</summary>
    [Fact]
    public void YearlyEntitlement_DoesNotApplyAgeException_BeforeOneYearSeniority()
    {
        var start = new DateOnly(2024, 1, 1);
        var type = AnnualType(accrualFrequency: AccrualFrequency.Monthly, accrualTiming: AccrualTiming.StartOfPeriod);
        var youngBirthDate = new DateOnly(2007, 6, 1); // 17 yaşında (accrualDate'te)

        var accrualDate = start; // years = 0 (henüz 1 yıl dolmadı)

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, youngBirthDate, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe(14m); // AnnualDays tabanı (Tier1) — 20 günlük yaş istisnası tabanı UYGULANMAZ
    }

    [Fact]
    public void YearlyEntitlement_AppliesAgeException_AfterOneYearSeniority()
    {
        var start = new DateOnly(2024, 1, 1);
        var type = AnnualType(accrualFrequency: AccrualFrequency.Monthly, accrualTiming: AccrualTiming.StartOfPeriod);
        var youngBirthDate = new DateOnly(2007, 6, 1); // aynı çalışan, bir yıl sonra hâlâ 18 yaş altı

        var accrualDate = start.AddYears(1); // years = 1

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, youngBirthDate, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe(20m); // kıdem 1 yılı doldurunca yaş istisnası tabanı devreye girer
    }

    /// <summary>senseik-007 (A-01 regresyon): 50+ yaş istisnası, 18- istisnasıyla simetrik olarak md.53/IV tabanını
    /// (AgeExceptionMinDays=20) tetiklemeli - kademe tabanı (Tier1=14) düşük kaldığında.</summary>
    [Fact]
    public void AnnualType_Age50OrOlder_AppliesTwentyDayFloor()
    {
        var start = new DateOnly(2020, 1, 1);
        var type = AnnualType();
        var accrualDate = start.AddYears(1);
        var oldBirthDate = accrualDate.AddYears(-55);

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, oldBirthDate, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe(20m);
    }

    /// <summary>Symmetric negative case: below the 50 floor (and below the tier2 threshold) the type's own
    /// seniority tiers alone decide the day count - the age exception must not fire.</summary>
    [Fact]
    public void AnnualType_UnderFifty_UsesSeniorityTiersOnly()
    {
        var start = new DateOnly(2020, 1, 1);
        var type = AnnualType();
        var accrualDate = start.AddYears(1);
        var underFiftyBirthDate = accrualDate.AddYears(-49);

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, underFiftyBirthDate, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe(14m);
    }

    /// <summary>Boundary: age == OldAgeMin (50) is inclusive ("age &gt;= OldAgeMin" in LeaveBalanceEngine), so the
    /// floor must already apply at exactly 50, not only strictly above it.</summary>
    [Fact]
    public void AnnualType_AgeExactlyOldAgeMin_AppliesTwentyDayFloor()
    {
        var start = new DateOnly(2020, 1, 1);
        var type = AnnualType();
        var accrualDate = start.AddYears(1);
        var exactlyFiftyBirthDate = accrualDate.AddYears(-StatutoryAnnualLeave.Turkey2026.OldAgeMin);

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, exactlyFiftyBirthDate, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe(20m);
    }

    /// <summary>When seniority alone already exceeds the age-exception floor (tier3, 26 &gt; 20), the tier wins -
    /// the floor never pulls the entitlement DOWN, only up.</summary>
    [Fact]
    public void AnnualType_SeniorityTierAboveFloor_TierWinsOverAgeFloor()
    {
        var start = new DateOnly(2005, 1, 1);
        var type = AnnualType();
        var accrualDate = start.AddYears(15);
        var fiftyBirthDate = accrualDate.AddYears(-50);

        LeaveBalanceEngine.YearlyEntitlement(type, start, accrualDate, fiftyBirthDate, StatutoryAnnualLeave.Turkey2026)
            .ShouldBe(26m);
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
    public void Project_ResetCarryOver_TotalsCoverOnlyTheCurrentPeriod()
    {
        var type = AnnualType(CarryOverMode.Reset, annualDays: 7m, accrualTiming: AccrualTiming.StartOfPeriod);
        var start = new DateOnly(2022, 1, 10);
        var today = new DateOnly(2026, 9, 15);
        var previous = LeaveBalanceEngine.Regenerate(type, TenantId, EmployeeId, start, null, today, [], null);
        var usage = LeaveLedgerEntry.Usage(TenantId, EmployeeId, type.Id, 3m, new DateOnly(2025, 5, 5), LeaveLedgerSource.Request, null);

        var balance = LeaveBalanceEngine.Project(type, start, today, [usage, .. previous]);

        balance.TotalAccrued.ShouldBe(7m);
        balance.TotalUsed.ShouldBe(0m);
        balance.CurrentBalance.ShouldBe(7m);
    }

    [Fact]
    public void Project_UnlimitedCarryOver_TotalAccruedKeepsLifetimeAccruals()
    {
        var type = AnnualType(CarryOverMode.Unlimited, annualDays: 20m, accrualTiming: AccrualTiming.StartOfPeriod);
        var start = new DateOnly(2024, 1, 10);
        var today = new DateOnly(2026, 9, 15);
        var generated = LeaveBalanceEngine.Regenerate(type, TenantId, EmployeeId, start, null, today, [], null);

        var balance = LeaveBalanceEngine.Project(type, start, today, generated);

        balance.TotalAccrued.ShouldBe(generated.Where(e => e.Type == LeaveLedgerEntryType.Accrual).Sum(e => e.Amount));
        balance.TotalAccrued.ShouldBeGreaterThan(balance.LastPeriodAccrued);
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

    /// <summary>QA teslim testi bulgu 7: 2026 payroll parameters verified against the official announcements
    /// (asgari ücret ÇSGB, GVK Genel Tebliği Sıra No 332, Hazine ve Maliye Bakanlığı kıdem tavanı genelgeleri).</summary>
    [Fact]
    public void PayrollSeed_Holds_The_Verified2026StatutoryValues()
    {
        var values = SeedValues();
        var mid2026 = new DateOnly(2026, 3, 15);

        values.Resolve("Payroll.MinimumWage.Gross", mid2026)!.DecimalValue.ShouldBe(33_030.00m);
        values.Resolve("Payroll.Sgk.Floor", mid2026)!.DecimalValue.ShouldBe(33_030.00m);
        values.Resolve("Payroll.Sgk.Ceiling", mid2026)!.DecimalValue.ShouldBe(247_725.00m);
        values.Resolve("Payroll.StampTax.Rate", mid2026)!.DecimalValue.ShouldBe(0.00759m);
        values.Resolve("Payroll.IncomeTax.Brackets", mid2026)!.ValueJson.ShouldBe(
            """[{"upperBound":190000,"rate":0.15},{"upperBound":400000,"rate":0.20},{"upperBound":1500000,"rate":0.27},{"upperBound":5300000,"rate":0.35},{"upperBound":null,"rate":0.40}]""");
        values.Resolve("Severance.CeilingGross", new DateOnly(2026, 6, 30))!.DecimalValue.ShouldBe(64_948.77m);
        values.Resolve("Severance.CeilingGross", new DateOnly(2026, 7, 1))!.DecimalValue.ShouldBe(73_729.87m);
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

        // senseik-036: yeni anahtarlar da seed'den okunur (md.74 çoğul gebelik ve süt izni 7578'den etkilenmez).
        after.BreastfeedingHoursPerDay.ShouldBe(1.5m);
        after.DisabledChildDaysPerYear.ShouldBe(10m);
        after.JobSearchMinHoursPerDay.ShouldBe(2m);
    }

    [Fact]
    public void Breastfeeding_IsSeededAndUnaffectedBy7578() =>
        SeedValues().Resolve(LegislationParameterKeys.BreastfeedingHoursPerDay, new DateOnly(2003, 6, 10))!.DecimalValue.ShouldBe(1.5m);
}

public sealed class AbsenceRecordTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid RecordedByUserId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 9, 15);

    [Fact]
    public void Record_Unexcused_Succeeds()
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Unexcused, null, "İşe gelmedi, haber vermedi", RecordedByUserId);

        record.IsSuccess.ShouldBeTrue();
        record.Value.Kind.ShouldBe(AbsenceKind.Unexcused);
        record.Value.Hours.ShouldBeNull();
        record.Value.ReversesId.ShouldBeNull();
        record.Value.IsReversal.ShouldBeFalse();
    }

    [Fact]
    public void Record_Late_WithHours_Succeeds()
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Late, 1.5m, null, RecordedByUserId);

        record.IsSuccess.ShouldBeTrue();
        record.Value.Hours.ShouldBe(1.5m);
    }

    [Fact]
    public void Record_HoursOnNonLateKind_Fails()
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Excused, 2m, null, RecordedByUserId);

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceHoursOnlyForLate);
    }

    [Fact]
    public void Record_LateWithoutHours_Fails()
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Late, null, null, RecordedByUserId);

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceHoursRequiredForLate);
    }

    [Theory]
    [InlineData(AbsenceKind.Unexcused)]
    [InlineData(AbsenceKind.Excused)]
    public void Record_NonLateWithoutHours_Succeeds(AbsenceKind kind)
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, kind, null, null, RecordedByUserId);

        record.IsSuccess.ShouldBeTrue();
        record.Value.Hours.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public void Record_HoursOutOfRange_Fails(decimal hours)
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Late, hours, null, RecordedByUserId);

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceHoursInvalid);
    }

    [Fact]
    public void Record_NoteTooLong_Fails()
    {
        var record = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Unexcused, null, new string('a', LeaveLimits.AbsenceNoteMaxLength + 1), RecordedByUserId);

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceNoteTooLong);
    }

    [Fact]
    public void Reverse_Valid_ProducesLinkedRecordWithoutHours()
    {
        var original = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Late, 1m, null, RecordedByUserId).Value;

        var reversal = AbsenceRecord.Reverse(original, "Yanlış tarihe girildi", RecordedByUserId, alreadyReversed: false);

        reversal.IsSuccess.ShouldBeTrue();
        reversal.Value.ReversesId.ShouldBe(original.Id);
        reversal.Value.IsReversal.ShouldBeTrue();
        reversal.Value.Hours.ShouldBeNull();
        reversal.Value.EmployeeId.ShouldBe(original.EmployeeId);
        reversal.Value.Date.ShouldBe(original.Date);
        reversal.Value.Kind.ShouldBe(original.Kind);
    }

    [Fact]
    public void Reverse_AlreadyReversed_Fails()
    {
        var original = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Unexcused, null, null, RecordedByUserId).Value;

        var reversal = AbsenceRecord.Reverse(original, "Tekrar deneme", RecordedByUserId, alreadyReversed: true);

        reversal.IsFailure.ShouldBeTrue();
        reversal.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceAlreadyReversed);
    }

    [Fact]
    public void Reverse_ARecordThatIsItselfAReversal_Fails()
    {
        var original = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Unexcused, null, null, RecordedByUserId).Value;
        var reversal = AbsenceRecord.Reverse(original, "Düzeltme", RecordedByUserId, alreadyReversed: false).Value;

        var doubleReversal = AbsenceRecord.Reverse(reversal, "Tekrar düzeltme", RecordedByUserId, alreadyReversed: false);

        doubleReversal.IsFailure.ShouldBeTrue();
        doubleReversal.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceReversalNotReversible);
    }

    [Fact]
    public void Reverse_EmptyReason_Fails()
    {
        var original = AbsenceRecord.Record(TenantId, EmployeeId, Date, AbsenceKind.Unexcused, null, null, RecordedByUserId).Value;

        var reversal = AbsenceRecord.Reverse(original, "  ", RecordedByUserId, alreadyReversed: false);

        reversal.IsFailure.ShouldBeTrue();
        reversal.Error.Code.ShouldBe(LeaveDomainErrors.AbsenceReversalReasonRequired);
    }
}
