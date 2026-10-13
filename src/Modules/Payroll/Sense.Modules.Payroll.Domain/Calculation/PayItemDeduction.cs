namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// One formula-driven net deduction (PAY-DEDUCTION) already evaluated by the Application layer: it never touches the gross or the
/// SGK/GV/damga bases, it is taken off the net after the taxes. The engine only caps the deductions so the net cannot go negative.
/// </summary>
/// <param name="Code">Pay item definition code (shown in the trace and on the payslip; the cap is applied from the last code backwards).</param>
/// <param name="Name">Display name of the pay item.</param>
/// <param name="Amount">Deduction amount for the month (2 decimals, never negative).</param>
/// <param name="Formula">The user's formula text, echoed into the trace.</param>
/// <param name="UsedVariables">Variables the formula referenced and their values (lower-case names), echoed into the trace.</param>
public sealed record PayItemDeduction(
    string Code,
    string Name,
    decimal Amount,
    string Formula,
    IReadOnlyDictionary<string, decimal> UsedVariables);

/// <summary>
/// Caps formula deductions at the net that is left after icra/nafaka, avans and sendika aidatı so the net never goes negative. Deterministic: the
/// excess is taken off the deductions from the LAST code backwards (code order, ordinal), each down to 0 at most.
/// </summary>
internal static class PayItemDeductionCap
{
    /// <param name="deductions">Already in code order.</param>
    /// <param name="remainingNet">The net that is left (never negative).</param>
    /// <returns>The applied amount per deduction (same order) and the codes that were reduced, in the order they were cut (last code first).</returns>
    public static (IReadOnlyList<decimal> Applied, IReadOnlyList<string> CappedCodes) Apply(IReadOnlyList<PayItemDeduction> deductions, decimal remainingNet)
    {
        var applied = deductions.Select(d => d.Amount).ToArray();
        var excess = applied.Sum() - Math.Max(0m, remainingNet);
        var capped = new List<string>();
        for (var i = applied.Length - 1; i >= 0 && excess > 0m; i--)
        {
            var cut = Math.Min(applied[i], excess);
            if (cut <= 0m)
            {
                continue;
            }

            applied[i] -= cut;
            excess -= cut;
            capped.Add(deductions[i].Code);
        }

        return (applied, capped);
    }
}
