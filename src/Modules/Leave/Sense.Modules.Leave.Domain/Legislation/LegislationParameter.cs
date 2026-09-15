using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Leave.Domain.Legislation;

/// <summary>
/// Date-ranged statutory parameter (ADR 0008), system-wide (not tenant data). Hosted by the Leave module until a
/// dedicated Legislation area exists; other modules read it only via Sense.Shared.Contracts.Legislation.ILegislationParameterReader.
/// </summary>
public sealed class LegislationParameter : Entity<Guid>
{
    private LegislationParameter()
    {
    }

    public LegislationParameter(
        Guid id, string key, string country, decimal? decimalValue, string? valueJson, DateOnly effectiveFrom, DateOnly? effectiveTo, string source)
        : base(id)
    {
        Key = key;
        Country = country;
        DecimalValue = decimalValue;
        ValueJson = valueJson;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Source = source;
    }

    public string Key { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 ("TR").</summary>
    public string Country { get; private set; } = string.Empty;

    public decimal? DecimalValue { get; private set; }

    public string? ValueJson { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Inclusive; null = still in force.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Legal reference (law/article, Official Gazette date/number).</summary>
    public string Source { get; private set; } = string.Empty;

    public bool Covers(DateOnly date) => EffectiveFrom <= date && (EffectiveTo is null || date <= EffectiveTo);
}
