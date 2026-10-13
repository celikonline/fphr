using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Domain.Incentives;

/// <summary>
/// A rate that is either a literal value (tenant data approved by the SMMM) or a reference to a
/// <c>LegislationParameter</c> key; never both. Rates are never hard-coded in the calculation.
/// </summary>
public sealed record IncentiveRate
{
    public decimal? Value { get; init; }

    public string? LegislationKey { get; init; }

    public bool IsResolvable => (Value is not null) != (LegislationKey is not null);

    public static IncentiveRate Literal(decimal value) => new() { Value = value };

    public static IncentiveRate Key(string legislationKey) => new() { LegislationKey = legislationKey };
}

/// <summary>
/// Kind-specific parameters of an <see cref="IncentiveDefinition"/> (same shape in the API and in the jsonb column).
/// A draft may be saved incomplete: only the field formats are validated on save, completeness is required on approval.
/// </summary>
public sealed record IncentiveParameters
{
    public const string LegislationKeyPrefix = "Payroll.";
    public const int LegislationKeyMaxLength = 100;
    public const decimal MaxCapMultiplier = 10m;

    public static readonly IncentiveParameters Empty = new();

    public IncentiveRate? Rate { get; init; }

    public IncentiveCoverageBase? CoverageBase { get; init; }

    public decimal? CapMultiplier { get; init; }

    public IncentiveRate? EmployerSgkRate { get; init; }

    public bool CoversEmployerSgk { get; init; }

    public bool CoversEmployerUnemployment { get; init; }

    public bool CoversEmployeeSgk { get; init; }

    public bool CoversEmployeeUnemployment { get; init; }

    internal bool HasCoverageFields =>
        CoverageBase is not null || CapMultiplier is not null || EmployerSgkRate is not null
        || CoversEmployerSgk || CoversEmployerUnemployment || CoversEmployeeSgk || CoversEmployeeUnemployment;

    /// <summary>Every legislation key referenced (their existence is checked by the Application layer on approval).</summary>
    public IReadOnlyList<string> LegislationKeys =>
        new[] { Rate?.LegislationKey, EmployerSgkRate?.LegislationKey }.OfType<string>().ToList();

    /// <summary>A deep copy: the definition owns its parameter objects (persisted as owned JSON), so shared instances such as
    /// <see cref="Empty"/> or a template's parameters must never be attached to two owners.</summary>
    public IncentiveParameters Copy() => this with { Rate = Rate is null ? null : Rate with { }, EmployerSgkRate = EmployerSgkRate is null ? null : EmployerSgkRate with { } };

    /// <summary>Field-format validation for saving a draft of the given kind.</summary>
    public Result Validate(IncentiveKind kind)
    {
        var rate = ValidateRate(Rate, "rate");
        if (rate.IsFailure)
        {
            return rate;
        }

        var employerSgkRate = ValidateRate(EmployerSgkRate, "employerSgkRate");
        if (employerSgkRate.IsFailure)
        {
            return employerSgkRate;
        }

        if (CapMultiplier is { } cap && (cap <= 0 || cap > MaxCapMultiplier))
        {
            return Invalid("capMultiplier");
        }

        return kind switch
        {
            IncentiveKind.DeclarationOnly when Rate is not null || HasCoverageFields => Invalid(Rate is not null ? "rate" : "coverageBase"),
            IncentiveKind.EmployerSgkRateDiscount or IncentiveKind.IncomeTaxWithholdingRelief when HasCoverageFields => Invalid("coverageBase"),
            _ => Result.Success(),
        };
    }

    /// <summary>The first missing parameter that blocks approval, or null when the kind's parameters are complete.</summary>
    public string? FirstMissingField(IncentiveKind kind) => kind switch
    {
        IncentiveKind.DeclarationOnly => null,
        IncentiveKind.EmployerSgkRateDiscount or IncentiveKind.IncomeTaxWithholdingRelief => Rate is { IsResolvable: true } ? null : "rate",
        _ => FirstMissingCoverageField(),
    };

    private string? FirstMissingCoverageField()
    {
        if (Rate is not { IsResolvable: true })
        {
            return "rate";
        }

        if (CoverageBase is null)
        {
            return "coverageBase";
        }

        if (CoverageBase == IncentiveCoverageBase.MinimumWage && CapMultiplier is null)
        {
            return "capMultiplier";
        }

        if (!(CoversEmployerSgk || CoversEmployerUnemployment || CoversEmployeeSgk || CoversEmployeeUnemployment))
        {
            return "coversEmployerSgk";
        }

        return CoversEmployerSgk && EmployerSgkRate is not { IsResolvable: true } ? "employerSgkRate" : null;
    }

    private static Result ValidateRate(IncentiveRate? rate, string field)
    {
        if (rate is null)
        {
            return Result.Success();
        }

        if (rate.Value is not null && rate.LegislationKey is not null)
        {
            return Invalid(field);
        }

        if (rate.Value is { } value && (value <= 0 || value > 1))
        {
            return Invalid(field);
        }

        if (rate.LegislationKey is { } key
            && (key.Length > LegislationKeyMaxLength || !key.StartsWith(LegislationKeyPrefix, StringComparison.Ordinal) || key.Length == LegislationKeyPrefix.Length))
        {
            return Error.Validation(PayrollDomainErrors.IncentiveLegislationKeyInvalid, ("field", field));
        }

        // A rate object with neither value nor key is an empty (draft) slot, not an error.
        return Result.Success();
    }

    private static Error Invalid(string field) => Error.Validation(PayrollDomainErrors.IncentiveParametersInvalid, ("field", field));
}
