namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// One formula-driven pay item (PAY-FORMULA) already evaluated by the Application layer and handed to the pure engine as a plain
/// gross earning: the engine never parses formulas. The three flags say which bases the amount joins; an exempt item is still part
/// of the gross (and of the employer cost) but is left out of the matching SGK/GV/damga base.
/// </summary>
/// <param name="Code">Pay item definition code (shown in the trace and on the payslip).</param>
/// <param name="Name">Display name of the pay item.</param>
/// <param name="GrossAmount">Gross amount for the month (2 decimals, never negative). For a net item this is the grossed-up amount.</param>
/// <param name="SgkSubject">Joins SGK_BASE.</param>
/// <param name="IncomeTaxSubject">Joins GV_BASE.</param>
/// <param name="StampTaxSubject">Joins STAMP_TAX.</param>
/// <param name="Formula">The user's formula text, echoed into the trace.</param>
/// <param name="UsedVariables">Variables the formula referenced and their values (lower-case names), echoed into the trace.</param>
/// <param name="NetTarget">Set only for a net item: the net amount the employee is meant to keep; <paramref name="GrossAmount"/> is its brüt karşılığı.</param>
public sealed record PayItemEarning(
    string Code,
    string Name,
    decimal GrossAmount,
    bool SgkSubject,
    bool IncomeTaxSubject,
    bool StampTaxSubject,
    string Formula,
    IReadOnlyDictionary<string, decimal> UsedVariables,
    decimal? NetTarget = null);
