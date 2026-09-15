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
/// </summary>
public sealed record TraceStep(
    int Order,
    string Code,
    string TitleKey,
    IReadOnlyDictionary<string, string> Inputs,
    string Formula,
    decimal Output,
    IReadOnlyList<TraceParameterRef> ParameterRefs);

/// <summary>Result of <see cref="PayrollCalculator.Calculate"/>: the full step trace plus the headline amounts a <c>Payslip</c> stores.</summary>
public sealed record PayrollCalculationResult(
    IReadOnlyList<TraceStep> Steps,
    decimal GrossAmount,
    decimal NetAmount,
    decimal EmployerCost)
{
    public TraceStep Step(string code) => Steps.Single(s => s.Code == code);
}

/// <summary>Trace step codes, in the fixed order 11.3.7 defines for the brüt→net akışı (this slice stops before NET_TO_GROSS_ITER).</summary>
public static class PayrollTraceStepCodes
{
    public const string Days = "DAYS";
    public const string Earnings = "EARNINGS";
    public const string SgkBase = "SGK_BASE";
    public const string SgkEmployee = "SGK_EMPLOYEE";
    public const string UnempEmployee = "UNEMP_EMPLOYEE";
    public const string GvBase = "GV_BASE";
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
}
