namespace Sense.Modules.Payroll.Application.Calculation;

/// <summary>
/// A statutory <c>LegislationParameter</c> (ADR 0008) has no value effective on the requested date (e.g. a payroll
/// period or exit date outside the seeded years). Handlers translate it to a 422 business-rule error instead of
/// letting it surface as an unhandled 500.
/// </summary>
public sealed class LegislationParameterMissingException(string parameterKey, DateOnly asOf)
    : InvalidOperationException($"LegislationParameter '{parameterKey}' has no value effective on {asOf:yyyy-MM-dd} (ADR 0008 seed missing or not yet migrated).")
{
    public string ParameterKey { get; } = parameterKey;

    public DateOnly AsOf { get; } = asOf;
}

/// <summary>Maps <see cref="LegislationParameterMissingException"/> to the localized 422 business-rule error.</summary>
public static class LegislationMissingError
{
    public static Sense.Shared.Kernel.Results.Error From(LegislationParameterMissingException ex) =>
        Sense.Shared.Kernel.Results.Error.Rule(
            Sense.Modules.Payroll.Domain.PayrollDomainErrors.LegislationParameterMissing,
            ("parameter", ex.ParameterKey),
            ("date", ex.AsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
}
