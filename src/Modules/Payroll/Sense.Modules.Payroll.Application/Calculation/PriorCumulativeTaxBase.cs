using Sense.Modules.Payroll.Domain;

namespace Sense.Modules.Payroll.Application.Calculation;

/// <summary>The latest calculated income-tax base before a month, without carrying a previous year's base forward.</summary>
public static class PriorCumulativeTaxBase
{
    public static async Task<decimal> ReadAsync(
        IPayrollPeriodRepository periods, IPayslipRepository payslips, Guid employeeId, DateOnly asOf, CancellationToken cancellationToken)
    {
        for (var month = asOf.Month - 1; month >= 1; month--)
        {
            var period = await periods.FindByYearMonthAsync(asOf.Year, month, cancellationToken);
            if (period is null) continue;
            var payslip = await payslips.FindByEmployeeAndPeriodAsync(employeeId, period.Id, cancellationToken);
            if (payslip is not null) return payslip.CumulativeGvBase;
        }

        return 0m;
    }
}
