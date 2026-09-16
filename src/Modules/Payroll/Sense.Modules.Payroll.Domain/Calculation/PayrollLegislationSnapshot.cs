namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>One cumulative income-tax bracket: taxed at <see cref="Rate"/> up to <see cref="UpperBound"/> (null = unbounded, last bracket).</summary>
public sealed record TaxBracket(decimal? UpperBound, decimal Rate);

/// <summary>
/// Resolved <c>Payroll.*</c> LegislationParameter values (ADR 0008) for one calculation date, as plain values - not the
/// <c>ILegislationParameterReader</c> port itself, so Domain stays free of the Shared.Contracts DB dependency (Application
/// resolves the port and builds this). Mirrors how Leave's Domain takes <c>StatutoryAnnualLeave</c> instead of the reader.
/// </summary>
public sealed record PayrollLegislationSnapshot(
    IReadOnlyList<TaxBracket> IncomeTaxBrackets,
    decimal SgkFloor,
    decimal SgkCeiling,
    decimal SgkEmployeeRate,
    decimal UnemploymentEmployeeRate,
    decimal SgkEmployerRate,
    decimal UnemploymentEmployerRate,
    /// <summary>5-point SGK employer incentive rate (5510 md.81) - not applied in this slice; see <see cref="PayrollCalculator"/> EMPLOYER_DISCOUNT step.</summary>
    decimal SgkEmployerDiscountRate,
    decimal StampTaxRate,
    decimal MinimumWageGross,
    decimal MealIncomeTaxDailyExemption,
    /// <summary>Fraction of the daily gross minimum wage that is SGK-exempt for cash/meal-card meal benefits.</summary>
    decimal MealSgkDailyExemptionRate,
    /// <summary>Whether the meal+market card variant gets the same exemption as Cash/MealCardOnly (11.3.07-bordro.md "Meal.MarketCard.ExemptionApplies"; current draft: false, fully taxed).</summary>
    bool MealMarketCardExemptionApplies,
    DateOnly AsOf);
