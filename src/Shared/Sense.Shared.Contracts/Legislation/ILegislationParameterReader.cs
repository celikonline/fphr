using System.Text.Json;

namespace Sense.Shared.Contracts.Legislation;

/// <summary>
/// Date-ranged statutory parameter (ADR 0008). A value is valid on [EffectiveFrom, EffectiveTo] (both inclusive,
/// EffectiveTo null = still in force). Numeric parameters use <see cref="DecimalValue"/>; structured ones
/// <see cref="ValueJson"/>.
/// </summary>
public sealed record LegislationParameterValue(
    string Key,
    string Country,
    decimal? DecimalValue,
    string? ValueJson,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Source);

/// <summary>
/// Cross-module read port for statutory parameters (ADR 0008). Consumers: Leave (durations, tiers), Overtime,
/// Payroll (Faz 3), Employee job-security thresholds. Implemented by the owner module's Infrastructure (today:
/// Sense.Modules.Leave.Infrastructure) and registered by that module.
/// </summary>
public interface ILegislationParameterReader
{
    /// <summary>The value of <paramref name="key"/> in force on <paramref name="asOf"/>; null if none is defined for that date.</summary>
    Task<LegislationParameterValue?> FindAsync(
        string key, DateOnly asOf, string country = LegislationCountries.Turkey, CancellationToken cancellationToken = default);

    /// <summary>Every value (all date ranges) of the given keys, for callers evaluating many dates in memory.</summary>
    Task<IReadOnlyList<LegislationParameterValue>> ListAsync(
        IReadOnlyCollection<string> keys, string country = LegislationCountries.Turkey, CancellationToken cancellationToken = default);
}

/// <summary>Typed helpers over <see cref="ILegislationParameterReader"/>.</summary>
public static class LegislationParameterReaderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<decimal?> GetDecimalAsync(
        this ILegislationParameterReader reader, string key, DateOnly asOf, CancellationToken cancellationToken = default) =>
        (await reader.FindAsync(key, asOf, LegislationCountries.Turkey, cancellationToken).ConfigureAwait(false))?.DecimalValue;

    public static async Task<decimal> GetDecimalOrDefaultAsync(
        this ILegislationParameterReader reader, string key, DateOnly asOf, decimal fallback, CancellationToken cancellationToken = default) =>
        await reader.GetDecimalAsync(key, asOf, cancellationToken).ConfigureAwait(false) ?? fallback;

    public static async Task<T?> GetAsync<T>(
        this ILegislationParameterReader reader, string key, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var value = await reader.FindAsync(key, asOf, LegislationCountries.Turkey, cancellationToken).ConfigureAwait(false);
        return value?.ValueJson is { Length: > 0 } json ? JsonSerializer.Deserialize<T>(json, JsonOptions) : default;
    }

    /// <summary>In-memory resolution over a <see cref="ILegislationParameterReader.ListAsync"/> result.</summary>
    public static LegislationParameterValue? Resolve(this IEnumerable<LegislationParameterValue> values, string key, DateOnly asOf) =>
        values
            .Where(v => v.Key == key && v.EffectiveFrom <= asOf && (v.EffectiveTo is null || asOf <= v.EffectiveTo))
            .OrderByDescending(v => v.EffectiveFrom)
            .FirstOrDefault();
}

public static class LegislationCountries
{
    public const string Turkey = "TR";
}

/// <summary>Well-known parameter keys seeded by the owner module (values and sources: ADR 0008 seed table).</summary>
public static class LegislationParameterKeys
{
    public const string AnnualTier1Days = "Leave.Annual.Tier1.Days";
    public const string AnnualTier2AfterYears = "Leave.Annual.Tier2.AfterYears";
    public const string AnnualTier2Days = "Leave.Annual.Tier2.Days";
    public const string AnnualTier3AfterYears = "Leave.Annual.Tier3.AfterYears";
    public const string AnnualTier3Days = "Leave.Annual.Tier3.Days";
    public const string AnnualYoungAgeMax = "Leave.Annual.YoungAgeMax";
    public const string AnnualOldAgeMin = "Leave.Annual.OldAgeMin";
    public const string AnnualAgeExceptionMinDays = "Leave.Annual.AgeExceptionMinDays";
    public const string AnnualUndergroundExtraDays = "Leave.Annual.UndergroundExtraDays";
    public const string PaternityDays = "Leave.Paternity.Days";
    public const string MaternityPreBirthWeeks = "Leave.Maternity.PreBirthWeeks";
    public const string MaternityPostBirthWeeks = "Leave.Maternity.PostBirthWeeks";
    public const string MarriageDays = "Leave.Marriage.Days";
    public const string AdoptionDays = "Leave.Adoption.Days";
    public const string BereavementDays = "Leave.Bereavement.Days";
    public const string DisabledChildDaysPerYear = "Leave.DisabledChild.DaysPerYear";
    public const string FosterCareDays = "Leave.FosterCare.Days";
    public const string PaidMilitaryServiceDays = "Leave.PaidMilitaryService.Days";
    public const string JobSearchMinHoursPerDay = "Leave.JobSearch.MinHoursPerDay";

    /// <summary>senseik-036: 4857 md.74/son - 1 yaşından küçük çocuk için günde toplam süt izni saati.</summary>
    public const string BreastfeedingHoursPerDay = "Leave.Breastfeeding.HoursPerDay";
    public const string OvertimeAnnualLimitHours = "Overtime.AnnualLimitHours";
    public const string OvertimeCompTimeExpiryMonths = "Overtime.CompTimeExpiryMonths";
    public const string OvertimeConsentRevocationNoticeDays = "Overtime.ConsentRevocationNoticeDays";

    // Payroll (Faz 3 P1, 11.3.07-bordro.md "Hesap turnusolu"). Values seeded via a Leave migration (ADR 0008: single
    // owner table leave.legislation_parameters) - Payroll only reads them through ILegislationParameterReader.
    public const string PayrollIncomeTaxBrackets = "Payroll.IncomeTax.Brackets";
    public const string PayrollSgkFloor = "Payroll.Sgk.Floor";
    public const string PayrollSgkCeiling = "Payroll.Sgk.Ceiling";
    public const string PayrollSgkEmployeeRate = "Payroll.Sgk.EmployeeRate";
    public const string PayrollUnemploymentEmployeeRate = "Payroll.Unemployment.EmployeeRate";
    public const string PayrollSgkEmployerRate = "Payroll.Sgk.EmployerRate";
    public const string PayrollUnemploymentEmployerRate = "Payroll.Unemployment.EmployerRate";

    /// <summary>5-point SGK employer incentive rate (5510 md.81); seeded for future use, not applied by the P1 calculator (TODO P2+).</summary>
    public const string PayrollSgkEmployerDiscountRate = "Payroll.Sgk.EmployerDiscountRate";
    public const string PayrollStampTaxRate = "Payroll.StampTax.Rate";
    public const string PayrollMinimumWageGross = "Payroll.MinimumWage.Gross";
    public const string PayrollMealIncomeTaxDailyExemption = "Payroll.Meal.IncomeTax.DailyExemption";
    public const string PayrollMealSgkDailyExemptionRate = "Payroll.Meal.Sgk.DailyExemptionRate";

    /// <summary>Faz 3 P2 (11.3.07-bordro.md "Yemek varyant kuralları"): 0/1 decimal, whether the meal+market card variant gets the Cash/MealCardOnly exemption.</summary>
    public const string PayrollMealMarketCardExemptionApplies = "Payroll.Meal.MarketCard.ExemptionApplies";

    // Employee job-security scope check (Faz 1a E5a, 11.3.03 "İşten ayrılış sihirbazı"). Seeded via a Leave migration
    // (ADR 0008: single owner table leave.legislation_parameters) - Employee only reads them through this reader.
    public const string JobSecurityMinHeadcount = "JobSecurity.MinHeadcount";
    public const string JobSecurityMinSeniorityMonths = "JobSecurity.MinSeniorityMonths";

    // Collective dismissal notification duty (4857 md.29, Faz 1a E5a). No seed rows yet: CollectiveDismissalEvaluator
    // falls back to the statutory defaults, and seeding these belongs with the next Leave migration (single owner table).
    // Written contract / working-conditions document duty (4857 md.8, Faz 1a E5b): when no written contract exists,
    // the employer must hand the employee a working-conditions document within the deadline. Same fallback pattern:
    // DocumentRequirementMaterializationService applies the statutory defaults when no rows are seeded.
    public const string ContractWrittenRequiredMinMonths = "Contract.WrittenRequiredMinMonths";
    public const string ContractWorkingConditionsDocDeadlineDays = "Contract.WorkingConditionsDocDeadlineDays";

    public const string CollectiveDismissalThresholds = "CollectiveDismissal.Thresholds";
    public const string CollectiveDismissalNoticeDays = "CollectiveDismissal.NoticeDays";
    public const string CollectiveDismissalRehirePriorityMonths = "CollectiveDismissal.RehirePriorityMonths";
    public const string CollectiveDismissalTempWorkerBanMonths = "CollectiveDismissal.TempWorkerBanMonths";

    // Severance/notice estimate (senseik-011, F-06 "İşten ayrılış sihirbazı" - kıdem/ihbar tazminatı). Seeded via a
    // Leave migration (ADR 0008: single owner table leave.legislation_parameters) - Payroll only reads them through
    // ILegislationParameterReader, same pattern as the Payroll.* keys above.
    /// <summary>İş K. m.14 - kıdem tazminatı tavanı: azami brüt tutar bir hizmet yılı için ödenecek (6 ayda bir güncellenir).</summary>
    public const string SeveranceCeilingGross = "Severance.CeilingGross";

    /// <summary>İş K. m.17 - kıdem süresine göre ihbar önel tablosu (JSON dizi: kıdem gün üst sınırı + hafta sayısı).</summary>
    public const string NoticeWeeksBySeniorityTiers = "Notice.WeeksBySeniorityTiers";
}
