using Sense.Shared.Kernel;

namespace Sense.Modules.Payroll.Domain.Calculation;

/// <summary>One employee's payslip figures in a period.</summary>
public sealed record PayslipFigures(Guid EmployeeId, decimal Gross, decimal Net, decimal EmployerCost);

/// <summary>How an employee's payslip relates to the previous period's.</summary>
public enum ComparisonKind
{
    /// <summary>Paid in both periods.</summary>
    Both = 0,

    /// <summary>Only in the current period (new hire or first payslip).</summary>
    New = 1,

    /// <summary>Only in the previous period (left, or not paid this month).</summary>
    Left = 2,
}

public sealed record ComparisonRow(
    Guid EmployeeId,
    ComparisonKind Kind,
    decimal? CurrentGross,
    decimal? PreviousGross,
    decimal? CurrentNet,
    decimal? PreviousNet,
    decimal? GrossChangePercent,
    decimal? NetChangePercent,
    bool ExceedsThreshold);

public sealed record ComparisonTotals(int Headcount, decimal Gross, decimal Net, decimal EmployerCost);

public sealed record PeriodComparison(ComparisonTotals Current, ComparisonTotals Previous, IReadOnlyList<ComparisonRow> Rows);

/// <summary>
/// Month-over-month payroll comparison ("Önceki ayla karşılaştırma", %5 sapma incelemesi): totals of both periods and, per
/// employee, the gross/net change. A row <see cref="ComparisonRow.ExceedsThreshold"/> when someone is new or gone, or when
/// gross or net moved by more than the threshold (a change from zero counts as exceeding).
/// </summary>
public static class PeriodComparisonCalculator
{
    /// <summary>The review threshold the closing checklist document names (5%); callers can pass another.</summary>
    public const decimal DefaultThresholdPercent = 5m;

    public static PeriodComparison Compare(
        IReadOnlyCollection<PayslipFigures> current, IReadOnlyCollection<PayslipFigures> previous, decimal thresholdPercent = DefaultThresholdPercent)
    {
        Guard.Against(thresholdPercent < 0m, "thresholdPercent must not be negative.");

        var currentById = current.ToDictionary(p => p.EmployeeId);
        var previousById = previous.ToDictionary(p => p.EmployeeId);
        var ids = currentById.Keys.Union(previousById.Keys);

        var rows = ids.Select(id => Row(id, currentById.GetValueOrDefault(id), previousById.GetValueOrDefault(id), thresholdPercent))
            .OrderByDescending(r => r.ExceedsThreshold)
            .ThenByDescending(r => Math.Abs(r.GrossChangePercent ?? 0m))
            .ToList();

        return new PeriodComparison(Totals(current), Totals(previous), rows);
    }

    private static ComparisonRow Row(Guid id, PayslipFigures? now, PayslipFigures? before, decimal threshold)
    {
        if (before is null)
        {
            return new ComparisonRow(id, ComparisonKind.New, now!.Gross, null, now.Net, null, null, null, true);
        }

        if (now is null)
        {
            return new ComparisonRow(id, ComparisonKind.Left, null, before.Gross, null, before.Net, null, null, true);
        }

        var grossChange = ChangePercent(now.Gross, before.Gross);
        var netChange = ChangePercent(now.Net, before.Net);
        var exceeds = Exceeds(now.Gross, before.Gross, grossChange, threshold) || Exceeds(now.Net, before.Net, netChange, threshold);
        return new ComparisonRow(id, ComparisonKind.Both, now.Gross, before.Gross, now.Net, before.Net, grossChange, netChange, exceeds);
    }

    /// <summary>Percent change, or null when the base is zero (no meaningful percentage).</summary>
    private static decimal? ChangePercent(decimal now, decimal before) =>
        before == 0m ? null : decimal.Round((now - before) / before * 100m, 2, MidpointRounding.AwayFromZero);

    private static bool Exceeds(decimal now, decimal before, decimal? change, decimal threshold) =>
        change is null ? now != before : Math.Abs(change.Value) > threshold;

    private static ComparisonTotals Totals(IReadOnlyCollection<PayslipFigures> list) =>
        new(list.Count, list.Sum(p => p.Gross), list.Sum(p => p.Net), list.Sum(p => p.EmployerCost));
}
