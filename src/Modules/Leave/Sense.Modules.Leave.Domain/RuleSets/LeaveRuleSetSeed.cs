using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;

namespace Sense.Modules.Leave.Domain.RuleSets;

/// <summary>
/// Statutory durations resolved from LegislationParameter on the seeding date. Paternity/Maternity durations are
/// deliberately left as resolved (10 gün / 8+16 hafta, 7578 s.K. referanslı) - senseik-036 kartı 5 gün / 112 gün /
/// 126 gün (çoğul) istiyordu ama roadmap ile çelişiyor ve hukuki onay yok; onay gelene kadar DEĞİŞTİRİLMEDİ
/// (bkz. docs/handoffs/leave-rule-seed-corrections.md "Ertelenen kalemler").
/// </summary>
public sealed record StatutoryLeaveDurations(
    StatutoryAnnualLeave Annual,
    decimal PaternityDays,
    decimal MaternityPreBirthWeeks,
    decimal MaternityPostBirthWeeks,
    decimal MarriageDays,
    decimal BereavementDays,
    decimal FosterCareDays,
    decimal PaidMilitaryServiceDays,
    // 4857 md.74/son - 1 yaşından küçük çocuk için günde toplam süt izni saati.
    decimal BreastfeedingHoursPerDay,
    // 4857 Ek md.2 son fıkra - %70 engelli/süreğen hastalıklı çocuğu olan işçiye yılda azami gün.
    decimal DisabledChildDaysPerYear,
    // 4857 md.27 - yeni iş arama izninde günde en az saat.
    decimal JobSearchMinHoursPerDay)
{
    public static StatutoryLeaveDurations Turkey2026 { get; } =
        new(StatutoryAnnualLeave.Turkey2026, 10m, 8m, 16m, 3m, 3m, 10m, 21m, 1.5m, 10m, 2m);
}

/// <summary>"Örnek Türk İş Kanunu kuralı" - the default rule set every tenant starts with (11.3.5 seed list, seed version 2026.05).</summary>
public static class LeaveRuleSetSeed
{
    public const string DefaultRuleSetName = "leave.rule_set.seed.turkish_labour_law";
    private const int DaysPerWeek = 7;
    private const decimal RemoteWorkDaysPerYear = 40m;
    private const decimal ExcuseMaxDays = 2m;
    private const decimal BirthdayMaxDays = 1m;

    /// <summary>4857 md.56/3: yıllık iznin bölünen parçalarından en az birinin gün sayısı.</summary>
    public const decimal AnnualMinimumBlockDays = 10m;

    /// <summary>Günlük tek talebin üst sınırı (saatlik türlerde tek gün): 1 gün.</summary>
    private const decimal SingleDayMax = 1m;

    public static LeaveRuleSet CreateTurkishLabourLawRuleSet(Guid tenantId, StatutoryLeaveDurations d)
    {
        var set = LeaveRuleSet.Create(tenantId, DefaultRuleSetName, isDefault: true, isSystemSeed: true).Value;
        foreach (var settings in Types(d))
        {
            set.AddType(settings);
        }

        return set;
    }

    public static IEnumerable<LeaveTypeSettings> Types(StatutoryLeaveDurations d)
    {
        var a = d.Annual;
        // 4857 md.56/3: yıllık izin bölünürse bir bölüm 10 günden az olamaz. MinConsecutiveDays bu tür için "en az
        // ardışık gün" = asgari blok anlamındadır: LeaveRequestRules bunu talep başına değil, kalan hakkın bir 10
        // günlük bloğa hâlâ yetmesi olarak denetler (bkz. AnnualLeaveSplitRule).
        yield return new LeaveTypeSettings(
            LeaveTypeCode.Annual, "leave.type.annual", null, IsPaid: true, LeaveLimitType.PerYear,
            AnnualDays: a.Tier1Days, AgeExceptionEnabled: true, AgeExceptionMinDays: a.AgeExceptionMinDays,
            AccrualFrequency: AccrualFrequency.Yearly, AccrualTiming: AccrualTiming.EndOfPeriod, AccrualBasis: AccrualBasis.HireDate,
            CarryOverMode: CarryOverMode.Unlimited,
            SeniorityTiers:
            [
                new LeaveSeniorityTier(a.Tier2AfterYears, SeniorityComparison.GreaterThan, a.Tier2Days - a.Tier1Days),
                new LeaveSeniorityTier(a.Tier3AfterYears, SeniorityComparison.GreaterOrEqual, a.Tier3Days - a.Tier2Days),
            ],
            MinConsecutiveDays: AnnualMinimumBlockDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Excuse, "leave.type.excuse", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: ExcuseMaxDays, RequiresDescription: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Sick, "leave.type.sick", null, false, LeaveLimitType.Unlimited,
            RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true);
        // 4857 md.27: ihbar süresi içinde günde en az JobSearchMinHoursPerDay saat. "Limitsiz" değil: tek talep tek gündür
        // (PerRequest, en çok 1 gün) ve günde en az 2 saatten kısa olamaz (MinConsecutiveDays, gün kesri). "Yalnızca
        // ihbar süresi içinde" kısıtı uygulanmıyor: Employee modülünde ihbar süresi başlangıcını taşıyan bir veri/port yok
        // (sonlandırma yalnız etkin son iş günü kaydeder) - bkz. handoff "Ertelenen kalemler".
        yield return new LeaveTypeSettings(LeaveTypeCode.JobSearch, "leave.type.job_search", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: SingleDayMax, MinConsecutiveDays: LeavePartialDay.HoursFraction(d.JobSearchMinHoursPerDay),
            Unit: LeaveUnit.Hourly, IsStatutory: true, HourlyHalfDayThresholdMinutes: 180, HourlyHalfDayDays: LeavePartialDay.HalfDayFraction);
        yield return new LeaveTypeSettings(LeaveTypeCode.Marriage, "leave.type.marriage", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.MarriageDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Bereavement, "leave.type.bereavement", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.BereavementDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Paternity, "leave.type.paternity", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.PaternityDays, CountsNonWorkingDays: true, IsStatutory: true, EligibleGender: LeaveEligibleGender.Male);
        yield return new LeaveTypeSettings(LeaveTypeCode.Maternity, "leave.type.maternity", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: (d.MaternityPreBirthWeeks + d.MaternityPostBirthWeeks) * DaysPerWeek, CountsNonWorkingDays: true,
            RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true, EligibleGender: LeaveEligibleGender.Female);
        yield return new LeaveTypeSettings(LeaveTypeCode.PostnatalUnpaid, "leave.type.postnatal_unpaid", null, false, LeaveLimitType.Unlimited,
            CountsNonWorkingDays: true, CountsTowardAccrualService: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.PaidMilitaryService, "leave.type.paid_military_service", null, false, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.PaidMilitaryServiceDays, CountsNonWorkingDays: true, CountsTowardAccrualService: true, RequiresDocument: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Unpaid, "leave.type.unpaid", null, false, LeaveLimitType.Unlimited,
            CountsNonWorkingDays: true, CountsTowardAccrualService: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.FosterCare, "leave.type.foster_care", null, false, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.FosterCareDays, CountsNonWorkingDays: true, RequiresDocument: true, IsStatutory: true);
        // 4857 md.74/son - 1 yaşından küçük çocuğu olan kadın çalışana günde toplam BreastfeedingHoursPerDay saat süt
        // izni. Tek talep tek gündür (PerRequest, en çok 1 gün); MaxConsecutiveDays günlük 1,5 saat üst sınırını gün kesri
        // olarak uygular (senseik-036 md.3). Yaş (<1) koşulu Employee'deki çocuk verisinden okunmadığı için İK onayında.
        yield return new LeaveTypeSettings(LeaveTypeCode.Breastfeeding, "leave.type.breastfeeding", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: SingleDayMax, MaxConsecutiveDays: LeavePartialDay.HoursFraction(d.BreastfeedingHoursPerDay),
            Unit: LeaveUnit.Hourly, IsStatutory: true, EligibleGender: LeaveEligibleGender.Female,
            HourlyHalfDayThresholdMinutes: 180, HourlyHalfDayDays: LeavePartialDay.HalfDayFraction);
        // 4857 Ek md.2 son fıkra - %70 engelli veya süreğen hastalığı olan çocuğu bulunan işçiye yılda azami
        // DisabledChildDaysPerYear gün ücretli izin (senseik-036 md.4).
        yield return new LeaveTypeSettings(LeaveTypeCode.DisabledChild, "leave.type.disabled_child", null, true, LeaveLimitType.PerYear,
            AnnualDays: d.DisabledChildDaysPerYear, AccrualFrequency: AccrualFrequency.Yearly, AccrualTiming: AccrualTiming.StartOfPeriod,
            AccrualBasis: AccrualBasis.CalendarYear, CarryOverMode: CarryOverMode.Reset, RequiresDocument: true, IsSensitive: true,
            ShowTypeNameOnCalendar: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.RemoteWork, "leave.type.template.remote_work", null, true, LeaveLimitType.PerYear,
            AnnualDays: RemoteWorkDaysPerYear, AccrualFrequency: AccrualFrequency.Yearly, AccrualTiming: AccrualTiming.StartOfPeriod,
            AccrualBasis: AccrualBasis.CalendarYear, CarryOverMode: CarryOverMode.Reset, RequiresDescription: true, ShowTypeNameOnCalendar: true, IsDisabled: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Birthday, "leave.type.template.birthday", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: BirthdayMaxDays, IsDisabled: true, UseOnlyOnBirthday: true);
    }
}
