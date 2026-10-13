using System.Text.Json.Serialization;
using Sense.Modules.Payroll.Domain.Garnishments;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// One parameter the calculator read while producing a <see cref="TraceStep"/> (11.3.7 "Hesap turnusolu" ParameterRefs).
/// <paramref name="ValidFrom"/> is the LegislationParameter row's EffectiveFrom (ADR 0008), so the trace can show which
/// dated value was used even after the parameter changes later.
/// </summary>
public sealed record TraceParameterRef(string Key, decimal Value, DateOnly ValidFrom);

/// <summary>
/// One step of a <see cref="PayrollCalculationResult"/> (11.3.7 "Hesap turnusolu"): brüt→net akışının bir adımı.
/// <paramref name="Inputs"/> are stringified for display only (trace is a read-only audit document, not a re-computable
/// graph); <paramref name="Formula"/> is a human-readable description of what was computed, not an evaluable expression.
/// <paramref name="PayItem"/> is the pay item definition code and is only set on PAY_ITEM steps (omitted from the stored JSON otherwise,
/// so a payslip without pay items keeps a byte-identical trace).
/// </summary>
public sealed record TraceStep(
    int Order,
    string Code,
    string TitleKey,
    IReadOnlyDictionary<string, string> Inputs,
    string Formula,
    decimal Output,
    IReadOnlyList<TraceParameterRef> ParameterRefs,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PayItem = null);

/// <summary>Result of <see cref="PayrollCalculator.Calculate"/>: the full step trace plus the headline amounts a <c>Payslip</c> stores.</summary>
/// <param name="GarnishmentApplied">senseik-019 2c: per-order amount <see cref="GarnishmentCalculator"/> applied this
/// month, so the caller can post it to each <see cref="Garnishments.GarnishmentOrder"/>'s balance. Empty when the
/// input had no active orders.</param>
public sealed record PayrollCalculationResult(
    IReadOnlyList<TraceStep> Steps,
    decimal GrossAmount,
    decimal NetAmount,
    decimal EmployerCost,
    IReadOnlyList<GarnishmentAppliedAmount> GarnishmentApplied)
{
    public TraceStep Step(string code) => Steps.Single(s => s.Code == code);
}

/// <summary>Trace step codes, in the fixed order 11.3.7 defines for the brüt→net akışı (this slice stops before NET_TO_GROSS_ITER).</summary>
public static class PayrollTraceStepCodes
{
    public const string Days = "DAYS";
    public const string PayItem = "PAY_ITEM";
    public const string Earnings = "EARNINGS";
    public const string SgkBase = "SGK_BASE";
    public const string SgkEmployee = "SGK_EMPLOYEE";
    public const string UnempEmployee = "UNEMP_EMPLOYEE";
    public const string GvBase = "GV_BASE";
    public const string DisabilityReduction = "DISABILITY_REDUCTION";
    public const string CumulativeBase = "CUMULATIVE_BASE";
    public const string IncomeTax = "INCOME_TAX";
    public const string MinWageExemptionGv = "MINWAGE_EXEMPTION_GV";
    public const string StampTax = "STAMP_TAX";
    public const string MinWageExemptionDv = "MINWAGE_EXEMPTION_DV";
    public const string Deductions = "DEDUCTIONS";
    public const string Net = "NET";
    public const string EmployerSgk = "EMPLOYER_SGK";
    public const string EmployerDiscount = "EMPLOYER_DISCOUNT";
    public const string EmployerUnemp = "EMPLOYER_UNEMP";
    public const string EmployerCost = "EMPLOYER_COST";

    /// <summary>PAY-INCENTIVE (b): premiums covered by the state; present only when such an incentive is in force.</summary>
    public const string EmployerIncentive = "EMPLOYER_INCENTIVE";

    /// <summary>PAY-INCENTIVE (c): income tax relief (terkin); present only when such an incentive is in force.</summary>
    public const string IncomeTaxRelief = "INCOME_TAX_RELIEF";
}
