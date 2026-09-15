using System.Globalization;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>One bracket's contribution to the tax on a [from, to] slice of the cumulative GV base.</summary>
public sealed record IncomeTaxBracketSegment(decimal Rate, decimal Amount, decimal Tax);

/// <summary>
/// Turkish cumulative income-tax bracket math (GVK): tax is computed on the running cumulative GV_BASE for the
/// calendar year, so one month's tax is TaxUpTo(cumulativeBase) - TaxUpTo(priorCumulativeBase). Brackets are read
/// from LegislationParameter (ADR 0008, key Payroll.IncomeTax.Brackets) - never hardcoded here.
/// </summary>
public static class IncomeTaxMath
{
    /// <summary>Progressive tax on [0, baseAmount] (baseAmount clamped to &gt;= 0).</summary>
    public static decimal TaxUpTo(IReadOnlyList<TaxBracket> brackets, decimal baseAmount)
    {
        if (baseAmount <= 0m)
        {
            return 0m;
        }

        decimal tax = 0m;
        decimal lower = 0m;
        foreach (var bracket in brackets)
        {
            var upper = bracket.UpperBound ?? decimal.MaxValue;
            if (baseAmount <= lower)
            {
                break;
            }

            var segmentTop = Math.Min(baseAmount, upper);
            var amount = segmentTop - lower;
            if (amount > 0m)
            {
                tax += amount * bracket.Rate;
            }

            if (baseAmount <= upper)
            {
                break;
            }

            lower = upper;
        }

        return tax;
    }

    /// <summary>
    /// Per-bracket breakdown of the slice [from, to] of the cumulative GV base (to &gt;= from &gt;= 0) - the segments that
    /// make up one month's INCOME_TAX step, for the trace's bracket-breakdown requirement (11.3.7).
    /// </summary>
    public static IReadOnlyList<IncomeTaxBracketSegment> Breakdown(IReadOnlyList<TaxBracket> brackets, decimal from, decimal to)
    {
        if (to <= from)
        {
            return [];
        }

        var segments = new List<IncomeTaxBracketSegment>();
        decimal lower = 0m;
        foreach (var bracket in brackets)
        {
            var upper = bracket.UpperBound ?? decimal.MaxValue;
            var sliceLow = Math.Max(lower, from);
            var sliceHigh = Math.Min(upper, to);
            if (sliceHigh > sliceLow)
            {
                var amount = sliceHigh - sliceLow;
                segments.Add(new IncomeTaxBracketSegment(bracket.Rate, amount, decimal.Round(amount * bracket.Rate, 2, MidpointRounding.AwayFromZero)));
            }

            if (to <= upper)
            {
                break;
            }

            lower = upper;
        }

        return segments;
    }

    public static string FormatFormula(IReadOnlyList<IncomeTaxBracketSegment> segments) =>
        segments.Count == 0
            ? "0"
            : string.Join(" + ", segments.Select(s =>
                string.Create(CultureInfo.InvariantCulture, $"{s.Amount:0.####}*{s.Rate:0.####}")));
}
