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
    public const string OvertimeAnnualLimitHours = "Overtime.AnnualLimitHours";
    public const string OvertimeCompTimeExpiryMonths = "Overtime.CompTimeExpiryMonths";
    public const string OvertimeConsentRevocationNoticeDays = "Overtime.ConsentRevocationNoticeDays";
}
