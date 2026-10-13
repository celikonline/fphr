using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.PayItems.Formulas;

namespace Sense.Modules.Payroll.Application.PayItems;

/// <summary>
/// One pay item whose formula has been evaluated for an employee-month (amount rounded to 2 decimals, never negative).
/// <paramref name="IsDeduction"/> marks a net deduction (PAY-DEDUCTION): its tax flags are false and it never joins the gross.
/// </summary>
public sealed record EvaluatedPayItem(
    string Code,
    string Name,
    bool IsNet,
    decimal Amount,
    bool SgkSubject,
    bool IncomeTaxSubject,
    bool StampTaxSubject,
    string Formula,
    IReadOnlyDictionary<string, decimal> UsedVariables,
    bool IsDeduction = false);

/// <summary>
/// Outcome of evaluating one employee-month's pay items. <see cref="FailedCode"/> is the code of the first pay item whose formula
/// could not be evaluated (null on success); the caller skips the employee with <c>pay_item_formula_failed</c>.
/// <see cref="NegativeCodes"/> lists the items whose formula came out negative and were clamped to 0.
/// </summary>
public sealed record PayItemEvaluation(IReadOnlyList<EvaluatedPayItem> Items, IReadOnlyList<string> NegativeCodes, string? FailedCode)
{
    public bool Succeeded => FailedCode is null;

    /// <summary>The earning items (code order).</summary>
    public IReadOnlyList<EvaluatedPayItem> Earnings => Items.Where(i => !i.IsDeduction).ToList();

    /// <summary>The deduction items (code order); they come off the net and never enter the net-to-gross solver.</summary>
    public IReadOnlyList<EvaluatedPayItem> Deductions => Items.Where(i => i.IsDeduction).ToList();
}

/// <summary>Evaluates the assignments in force for one employee-month (the engine itself never sees formulas).</summary>
public static class PayItemEvaluator
{
    /// <summary>
    /// <paramref name="variablesFor"/> returns the variable map for an assignment's <c>deger</c> (assignment value, else the
    /// definition's default, else null). Items are evaluated in code order so the trace is deterministic. A formula error (including
    /// a stored formula that no longer parses) stops the evaluation at that item.
    /// </summary>
    public static PayItemEvaluation Evaluate(
        IEnumerable<(EmployeePayItemAssignment Assignment, PayItemDefinition Definition)> assigned,
        Func<decimal?, IReadOnlyDictionary<string, decimal>> variablesFor)
    {
        var items = new List<EvaluatedPayItem>();
        var negative = new List<string>();
        foreach (var (assignment, definition) in assigned.OrderBy(a => a.Definition.Code, StringComparer.Ordinal))
        {
            var parsed = PayrollFormula.Parse(definition.Formula);
            if (parsed.IsFailure)
            {
                return new PayItemEvaluation(items, negative, definition.Code);
            }

            var variables = variablesFor(assignment.Value ?? definition.DefaultValue);
            var evaluated = parsed.Value.Evaluate(variables);
            if (evaluated.IsFailure)
            {
                return new PayItemEvaluation(items, negative, definition.Code);
            }

            var amount = decimal.Round(evaluated.Value, 2, MidpointRounding.AwayFromZero);
            if (amount < 0m)
            {
                negative.Add(definition.Code);
                amount = 0m;
            }

            // Lazily evaluated branches (eger) may reference a variable that has no value; only variables with a value are echoed.
            var used = parsed.Value.UsedVariables.Where(variables.ContainsKey).ToDictionary(name => name, name => variables[name], StringComparer.Ordinal);
            items.Add(new EvaluatedPayItem(
                definition.Code, definition.Name, definition.AmountBasis == PayItemAmountBasis.Net, amount,
                definition.IsSgkSubject, definition.IsIncomeTaxSubject, definition.IsStampTaxSubject, definition.Formula, used,
                definition.Direction == PayItemDirection.Deduction));
        }

        return new PayItemEvaluation(items, negative, null);
    }
}

/// <summary>
/// Splits the single gross-up found for "all net extras of the month" (net adjustments plus net pay items) back onto the lines in
/// proportion to their net amounts; the kuruş difference goes to the last line that has a net amount.
/// </summary>
public static class PayItemNetAllocator
{
    /// <returns>The gross share of the net adjustments and one gross share per net pay item (same order as <paramref name="itemNets"/>).</returns>
    public static (decimal AdjustmentGross, IReadOnlyList<decimal> ItemGross) Allocate(
        decimal solvedGross, decimal adjustmentNet, IReadOnlyList<decimal> itemNets)
    {
        var parts = new List<decimal> { adjustmentNet };
        parts.AddRange(itemNets);
        var total = parts.Sum();
        var shares = new decimal[parts.Count];
        var last = parts.FindLastIndex(p => p > 0m);
        if (total <= 0m || last < 0)
        {
            return (0m, new decimal[itemNets.Count]);
        }

        var allocated = 0m;
        for (var i = 0; i < parts.Count; i++)
        {
            if (i == last || parts[i] <= 0m)
            {
                continue;
            }

            shares[i] = decimal.Round(solvedGross * parts[i] / total, 2, MidpointRounding.AwayFromZero);
            allocated += shares[i];
        }

        shares[last] = solvedGross - allocated;
        return (shares[0], shares.Skip(1).ToList());
    }
}
