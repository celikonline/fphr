using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Payroll.Application.Calculation;

/// <summary>JSON shape of one Payroll.IncomeTax.Brackets entry (ValueJson array), decimal for the last (unbounded) bracket's UpperBound.</summary>
public sealed record IncomeTaxBracketParameter(decimal? UpperBound, decimal Rate);

/// <summary>
/// Resolves the Payroll.* LegislationParameter values (ADR 0008) into the plain <see cref="PayrollLegislationSnapshot"/>
/// Domain's <see cref="PayrollCalculator"/> takes - this is the "Application layer resolves parameters via
/// ILegislationParameterReader and passes plain values in" seam the P1 task calls for.
/// </summary>
public static class PayrollLegislationSnapshotResolver
{
    public static async Task<PayrollLegislationSnapshot> ResolveAsync(
        ILegislationParameterReader reader, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var brackets = await reader.GetAsync<List<IncomeTaxBracketParameter>>(LegislationParameterKeys.PayrollIncomeTaxBrackets, asOf, cancellationToken)
            ?? throw MissingParameter(LegislationParameterKeys.PayrollIncomeTaxBrackets, asOf);

        return new PayrollLegislationSnapshot(
            brackets.Select(b => new TaxBracket(b.UpperBound, b.Rate)).ToList(),
            SgkFloor: await Decimal(reader, LegislationParameterKeys.PayrollSgkFloor, asOf, cancellationToken),
            SgkCeiling: await Decimal(reader, LegislationParameterKeys.PayrollSgkCeiling, asOf, cancellationToken),
            SgkEmployeeRate: await Decimal(reader, LegislationParameterKeys.PayrollSgkEmployeeRate, asOf, cancellationToken),
            UnemploymentEmployeeRate: await Decimal(reader, LegislationParameterKeys.PayrollUnemploymentEmployeeRate, asOf, cancellationToken),
            SgkEmployerRate: await Decimal(reader, LegislationParameterKeys.PayrollSgkEmployerRate, asOf, cancellationToken),
            UnemploymentEmployerRate: await Decimal(reader, LegislationParameterKeys.PayrollUnemploymentEmployerRate, asOf, cancellationToken),
            SgkEmployerDiscountRate: await Decimal(reader, LegislationParameterKeys.PayrollSgkEmployerDiscountRate, asOf, cancellationToken),
            StampTaxRate: await Decimal(reader, LegislationParameterKeys.PayrollStampTaxRate, asOf, cancellationToken),
            MinimumWageGross: await Decimal(reader, LegislationParameterKeys.PayrollMinimumWageGross, asOf, cancellationToken),
            MealIncomeTaxDailyExemption: await Decimal(reader, LegislationParameterKeys.PayrollMealIncomeTaxDailyExemption, asOf, cancellationToken),
            MealSgkDailyExemptionRate: await Decimal(reader, LegislationParameterKeys.PayrollMealSgkDailyExemptionRate, asOf, cancellationToken),
            AsOf: asOf);
    }

    private static async Task<decimal> Decimal(ILegislationParameterReader reader, string key, DateOnly asOf, CancellationToken cancellationToken) =>
        await reader.GetDecimalAsync(key, asOf, cancellationToken).ConfigureAwait(false) ?? throw MissingParameter(key, asOf);

    private static InvalidOperationException MissingParameter(string key, DateOnly asOf) =>
        new($"LegislationParameter '{key}' has no value effective on {asOf:yyyy-MM-dd} (ADR 0008 seed missing or not yet migrated).");
}
