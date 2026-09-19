using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Calendar;

namespace Sense.Modules.Leave.Domain.RuleSets;

/// <summary>
/// Statutory durations resolved from LegislationParameter on the seeding date. Paternity/Maternity durations
/// reflect 7578 s.K. (RG 01.05.2026/33240), which is in force as of this seed's date - see the
/// senseik-036 handoff for the legal verification behind not reverting these to the pre-7578 figures.
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
    // 4857 md.74 - çoğul gebelikte doğum öncesi süreye eklenen ilave hafta (7578 sonrası da korunur).
    decimal MaternityMultipleBirthPreBirthExtraWeeks,
    // 4857 md.74/son - 1 yaşından küçük çocuk için günde toplam süt izni saati.
    decimal BreastfeedingHoursPerDay,
    // 4857 Ek md.2 son fıkra - %70 engelli/süreğen hastalıklı çocuğu olan işçiye yılda azami gün.
    decimal DisabledChildDaysPerYear,
    // 4857 md.27 - yeni iş arama izninde günde en az saat.
    decimal JobSearchMinHoursPerDay)
{
    public static StatutoryLeaveDurations Turkey2026 { get; } =
        new(StatutoryAnnualLeave.Turkey2026, 10m, 8m, 16m, 3m, 3m, 10m, 21m, 2m, 1.5m, 10m, 2m);
}

/// <summary>"Örnek Türk İş Kanunu kuralı" - the default rule set every tenant starts with (11.3.5 seed list, seed version 2026.05).</summary>
public static class LeaveRuleSetSeed
{
    public const string DefaultRuleSetName = "leave.rule_set.seed.turkish_labour_law";
    private const int DaysPerWeek = 7;
    private const decimal RemoteWorkDaysPerYear = 40m;
    private const decimal ExcuseMaxDays = 2m;
    private const decimal BirthdayMaxDays = 1m;

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
        // senseik-036 md.6 (4857 md.56/3): NOT wired to MinConsecutiveDays deliberately - m.56/3 only requires
        // that when the entitlement IS split into installments, one installment be >= 10 days; it does not bar a
        // single short request. LeaveRequestRules.ValidateAsync enforces MinConsecutiveDays per individual
        // request, so setting it here would reject every annual-leave request under 10 days - a product-breaking
        // regression demonstrated by the existing test suite (LeaveRequestTests/LeaveManagementTests routinely
        // submit 1-2 day Annual requests through the full validation pipeline). See the senseik-036 handoff.
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
            IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Excuse, "leave.type.excuse", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: ExcuseMaxDays, RequiresDescription: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Sick, "leave.type.sick", null, false, LeaveLimitType.Unlimited,
            RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true);
        // 4857 md.27: bildirim/ihbar süresi içinde günde en az JobSearchMinHoursPerDay saat kullanılabilir; herhangi bir
        // toplam üst sınırı yok (Unlimited), ama tek bir talep bu alt sınırın altında olamaz (MinConsecutiveDays,
        // hafta.HoursFraction ile gün kesrine çevrilir). "Yalnızca ihbar süresi içinde kullanılabilir" kısıtı, Employee
        // modülünde henüz bir ihbar süresi/bildirim tarihi okuma portu bulunmadığından burada uygulanmıyor - bkz. handoff.
        yield return new LeaveTypeSettings(LeaveTypeCode.JobSearch, "leave.type.job_search", null, true, LeaveLimitType.Unlimited,
            MinConsecutiveDays: LeavePartialDay.HoursFraction(d.JobSearchMinHoursPerDay), Unit: LeaveUnit.Hourly, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Marriage, "leave.type.marriage", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.MarriageDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Bereavement, "leave.type.bereavement", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.BereavementDays, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Paternity, "leave.type.paternity", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.PaternityDays, CountsNonWorkingDays: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Maternity, "leave.type.maternity", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: (d.MaternityPreBirthWeeks + d.MaternityPostBirthWeeks) * DaysPerWeek, CountsNonWorkingDays: true,
            RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true);
        // 4857 md.74 - çoğul gebelikte doğum öncesi süreye +MaternityMultipleBirthPreBirthExtraWeeks eklenir (7578
        // sonrasında da korunan hüküm); tekil Maternity'den ayrı statik tür olarak modellenir (senseik-036 md.2).
        yield return new LeaveTypeSettings(LeaveTypeCode.MaternityMultipleBirth, "leave.type.maternity_multiple_birth", null, true, LeaveLimitType.PerRequest,
            PerRequestMaxDays: (d.MaternityPreBirthWeeks + d.MaternityMultipleBirthPreBirthExtraWeeks + d.MaternityPostBirthWeeks) * DaysPerWeek,
            CountsNonWorkingDays: true, RequiresDocument: true, IsSensitive: true, ShowTypeNameOnCalendar: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.PostnatalUnpaid, "leave.type.postnatal_unpaid", null, false, LeaveLimitType.Unlimited,
            CountsNonWorkingDays: true, CountsTowardAccrualService: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.PaidMilitaryService, "leave.type.paid_military_service", null, false, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.PaidMilitaryServiceDays, CountsNonWorkingDays: true, CountsTowardAccrualService: true, RequiresDocument: true, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.Unpaid, "leave.type.unpaid", null, false, LeaveLimitType.Unlimited,
            CountsNonWorkingDays: true, CountsTowardAccrualService: false, IsStatutory: true);
        yield return new LeaveTypeSettings(LeaveTypeCode.FosterCare, "leave.type.foster_care", null, false, LeaveLimitType.PerRequest,
            PerRequestMaxDays: d.FosterCareDays, CountsNonWorkingDays: true, RequiresDocument: true, IsStatutory: true);
        // 4857 md.74/son - 1 yaşından küçük çocuğu olan kadın çalışana günde toplam BreastfeedingHoursPerDay saat süt
        // izni; MaxConsecutiveDays tek bir taleple bu üst sınırı gün kesri olarak sınırlar (senseik-036 md.3).
        yield return new LeaveTypeSettings(LeaveTypeCode.Breastfeeding, "leave.type.breastfeeding", null, true, LeaveLimitType.Unlimited,
            MaxConsecutiveDays: LeavePartialDay.HoursFraction(d.BreastfeedingHoursPerDay), Unit: LeaveUnit.Hourly, IsStatutory: true);
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
            PerRequestMaxDays: BirthdayMaxDays, IsDisabled: true);
    }
}
