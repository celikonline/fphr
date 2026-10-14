using System.Globalization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Closing;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Legislation;

namespace Sense.Modules.Payroll.Application.Closing;

/// <summary>What approval asks of the closing checklist; a port so the approve handler can be tested without the rule inputs.</summary>
public interface IClosingChecklistGate
{
    /// <summary>
    /// Opens the period's checklist if needed, re-runs the automatic rules and reports whether a blocking check of the
    /// preparation or calculation stage is still failed or pending (waived counts as done).
    /// </summary>
    Task<bool> HasOpenBlockersBeforeApprovalAsync(PayrollPeriod period, CancellationToken cancellationToken);
}

/// <summary>
/// Opens a period's closing checklist from <see cref="ClosingCheckCatalog"/> and decides its automatic items from Payroll's own
/// data: statutory parameters for the month, payslips generated, none left "Değişti", none with a negative net.
/// The detail an automatic rule stores is just the number of offending items (the UI words it).
/// </summary>
public sealed class ClosingChecklistService(
    IPeriodClosingCheckRepository checks,
    IPayslipRepository payslips,
    IPayslipDirtyFlagRepository dirtyFlags,
    ILegislationParameterReader legislation,
    IEmployeeDirectory employeeDirectory,
    TimeProvider clock) : IClosingChecklistGate
{
    /// <summary>The period's checks, creating any that the catalog has and the period does not yet.</summary>
    public async Task<IReadOnlyList<PeriodClosingCheck>> EnsureAsync(PayrollPeriod period, CancellationToken cancellationToken)
    {
        var existing = (await checks.ListByPeriodAsync(period.Id, cancellationToken)).ToList();
        foreach (var definition in ClosingCheckCatalog.All.Where(d => existing.All(c => c.Code != d.Code)))
        {
            var created = PeriodClosingCheck.Create(period.TenantId, period.Id, definition);
            checks.Add(created);
            existing.Add(created);
        }

        return Order(existing);
    }

    /// <summary>Re-decides every automatic check; a waived one keeps its waiver.</summary>
    public async Task RunAutomaticAsync(PayrollPeriod period, IReadOnlyList<PeriodClosingCheck> list, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var check in list.Where(c => c.Kind == ClosingCheckKind.Automatic))
        {
            var (passed, detail) = await EvaluateAsync(check.Code, period, cancellationToken);
            check.ApplyRuleResult(passed, detail, now);
        }
    }

    public async Task<bool> HasOpenBlockersBeforeApprovalAsync(PayrollPeriod period, CancellationToken cancellationToken)
    {
        var list = await EnsureAsync(period, cancellationToken);
        await RunAutomaticAsync(period, list, cancellationToken);
        return list.Any(c => c.Stage <= ClosingStage.Calculation && c.Severity == ClosingCheckSeverity.Blocker && !c.IsSatisfied);
    }

    /// <summary>Catalog order (stage, then the template's own order) so the UI lists the checklist the way the source document does.</summary>
    public static IReadOnlyList<PeriodClosingCheck> Order(IEnumerable<PeriodClosingCheck> list)
    {
        var position = ClosingCheckCatalog.All.Select((d, i) => (d.Code, i)).ToDictionary(x => x.Code, x => x.i);
        return list.OrderBy(c => position.GetValueOrDefault(c.Code, int.MaxValue)).ToList();
    }

    private async Task<(bool Passed, string? Detail)> EvaluateAsync(string code, PayrollPeriod period, CancellationToken cancellationToken)
    {
        switch (code)
        {
            case "LEGISLATION_PARAMETERS":
                try
                {
                    await PayrollLegislationSnapshotResolver.ResolveAsync(legislation, new DateOnly(period.Year, period.Month, 1), cancellationToken);
                    return (true, null);
                }
                catch (LegislationParameterMissingException ex)
                {
                    return (false, ex.ParameterKey);
                }

            case "PAYSLIPS_GENERATED":
                var count = (await payslips.ListByPeriodAsync(period.Id, cancellationToken)).Count;
                return (count > 0, count.ToString(CultureInfo.InvariantCulture));

            case "NO_DIRTY_PAYSLIPS":
                var dirty = (await dirtyFlags.ListActiveByPeriodAsync(period.Id, cancellationToken)).Count;
                return (dirty == 0, dirty == 0 ? null : dirty.ToString(CultureInfo.InvariantCulture));

            case "NET_NOT_NEGATIVE":
                var bad = (await payslips.ListByPeriodAsync(period.Id, cancellationToken)).Count(p => p.NetAmount < 0m || p.GrossAmount <= 0m);
                return (bad == 0, bad == 0 ? null : bad.ToString(CultureInfo.InvariantCulture));

            case "EMPLOYEES_WITHOUT_PAYSLIP":
                // Use the same employment window as the payroll run, including exit-month employees.
                var paid = (await payslips.ListByPeriodAsync(period.Id, cancellationToken)).Select(p => p.EmployeeId).ToHashSet();
                var unpaid = (await employeeDirectory.ListPayrollCandidateEmployeeIdsAsync(period.TenantId,
                    new DateOnly(period.Year, period.Month, 1), new DateOnly(period.Year, period.Month, 1).AddMonths(1).AddDays(-1), cancellationToken)).Count(id => !paid.Contains(id));
                return (unpaid == 0, unpaid == 0 ? null : unpaid.ToString(CultureInfo.InvariantCulture));

            default:
                // A catalog code with no rule yet stays failed rather than passing silently.
                return (false, null);
        }
    }
}
