using Sense.Modules.Payroll.Application.Calculation;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.ExitSettlements;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Severance;

/// <summary>
/// Nets the lines of an <see cref="ExitSettlement"/> (SGK, gelir vergisi, damga vergisi) as of its termination date, using the
/// legislation in force then and the employee's cumulative GV base carried over from earlier months of that year - the same
/// carry-over the monthly payroll run reads. Shared by the exit event handler and the line commands.
/// </summary>
public sealed class ExitSettlementNetService(
    ILegislationParameterReader legislation, IPayrollPeriodRepository periods, IPayslipRepository payslips)
{
    /// <summary>Recomputes and stores the net of <paramref name="settlement"/>; on missing legislation the settlement is left as it was and the error is returned.</summary>
    public async Task<Result> RecalculateAsync(ExitSettlement settlement, CancellationToken cancellationToken)
    {
        PayrollLegislationSnapshot snapshot;
        try
        {
            snapshot = await PayrollLegislationSnapshotResolver.ResolveAsync(legislation, settlement.TerminationDate, cancellationToken);
        }
        catch (LegislationParameterMissingException ex)
        {
            return LegislationMissingError.From(ex);
        }

        var prior = await PriorCumulativeGvBaseAsync(settlement, cancellationToken);
        settlement.ApplyNet(ExitSettlementNetCalculator.Calculate(settlement.PayInputs(), prior, snapshot));
        return Result.Success();
    }

    /// <summary>
    /// Cumulative GV base of the latest payslip in the exit year before the exit month (0 in January, or when the employee
    /// has no earlier payslip that year - then the brackets may be underreported, as in the monthly run).
    /// </summary>
    private async Task<decimal> PriorCumulativeGvBaseAsync(ExitSettlement settlement, CancellationToken cancellationToken)
    {
        var year = settlement.TerminationDate.Year;
        for (var month = settlement.TerminationDate.Month - 1; month >= 1; month--)
        {
            var period = await periods.FindByYearMonthAsync(year, month, cancellationToken);
            if (period is null)
            {
                continue;
            }

            var payslip = await payslips.FindByEmployeeAndPeriodAsync(settlement.EmployeeId, period.Id, cancellationToken);
            if (payslip is not null)
            {
                return payslip.CumulativeGvBase;
            }
        }

        return 0m;
    }
}
