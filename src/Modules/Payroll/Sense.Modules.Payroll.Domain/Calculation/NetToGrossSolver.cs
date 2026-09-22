using System.Globalization;
using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>
/// Net→brüt ("net ücretin brütleştirilmesi"): for an employee whose salary is agreed as a fixed <i>net</i> amount, finds the gross the
/// payroll must run on so that what is left after SGK, işsizlik primi, gelir vergisi and damga vergisi equals that net. It drives the
/// real <see cref="PayrollCalculator"/> (so brackets, cumulative base, exemptions and disability reduction all apply exactly as in a
/// normal run) and bisects on the gross: the net left is strictly increasing in the gross, so the search always converges.
/// </summary>
public static class NetToGrossSolver
{
    private const decimal Tolerance = 0.005m;
    private const int MaxIterations = 80;

    /// <summary>Upper bound of the search as a multiple of the net; even the top bracket plus premiums leaves well over a third net.</summary>
    private const decimal UpperBoundFactor = 4m;

    /// <summary>
    /// The gross (to kuruş) whose calculation leaves <paramref name="targetNet"/> as salary net. <paramref name="calculate"/> must run the
    /// calculator for a given gross <b>without</b> additions or deductions (meal, expenses, garnishments), so the figure compared is the salary net only.
    /// </summary>
    public static decimal Solve(decimal targetNet, Func<decimal, PayrollCalculationResult> calculate)
    {
        Guard.Against(targetNet <= 0m, "targetNet must be positive.");

        var low = targetNet;
        var high = targetNet * UpperBoundFactor;
        for (var i = 0; i < MaxIterations && high - low > Tolerance; i++)
        {
            var mid = (low + high) / 2m;
            if (SalaryNet(calculate(mid)) < targetNet)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return decimal.Round((low + high) / 2m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Net prim: the extra <b>gross</b> that, added on top of the month's other earnings, raises the employee's net by exactly <paramref name="netExtra"/>.
    /// <paramref name="calculate"/> runs the month with a given extra gross; the marginal tax makes the gross-up larger than the net.
    /// </summary>
    public static decimal SolveExtra(decimal netExtra, Func<decimal, PayrollCalculationResult> calculate)
    {
        Guard.Against(netExtra <= 0m, "netExtra must be positive.");

        var target = SalaryNet(calculate(0m)) + netExtra;
        var low = netExtra;
        var high = netExtra * UpperBoundFactor;
        for (var i = 0; i < MaxIterations && high - low > Tolerance; i++)
        {
            var mid = (low + high) / 2m;
            if (SalaryNet(calculate(mid)) < target)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return decimal.Round((low + high) / 2m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Salary net of a calculation: gross minus the employee's premiums and the taxes payable, read from the NET step's inputs.</summary>
    public static decimal SalaryNet(PayrollCalculationResult result)
    {
        var inputs = result.Step(PayrollTraceStepCodes.Net).Inputs;
        decimal Read(string key) => decimal.Parse(inputs[key], CultureInfo.InvariantCulture);
        return Read("gross") - Read("sgkEmployee") - Read("unempEmployee") - Read("incomeTaxAfterExemption") - Read("stampTaxAfterExemption");
    }
}
