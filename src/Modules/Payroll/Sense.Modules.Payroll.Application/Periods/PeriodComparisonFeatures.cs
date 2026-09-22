using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>One employee's line of the month-over-month comparison, with the employee's name.</summary>
public sealed record ComparisonLineDto(
    Guid EmployeeId,
    string EmployeeName,
    ComparisonKind Kind,
    decimal? CurrentGross,
    decimal? PreviousGross,
    decimal? CurrentNet,
    decimal? PreviousNet,
    decimal? GrossChangePercent,
    decimal? NetChangePercent,
    bool ExceedsThreshold);

/// <summary>The period against the month before it. <see cref="PreviousPeriodId"/> is null when no earlier month was opened (then the previous side is empty).</summary>
public sealed record PeriodComparisonDto(
    Guid PeriodId,
    Guid? PreviousPeriodId,
    int PreviousYear,
    int PreviousMonth,
    decimal ThresholdPercent,
    ComparisonTotals Current,
    ComparisonTotals Previous,
    int ExceedingCount,
    IReadOnlyList<ComparisonLineDto> Lines);

/// <summary>Compares a period with the previous calendar month ("Önceki ayla karşılaştırma"); rows past the threshold (default 5%) come first.</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record GetPeriodComparisonQuery(Guid PeriodId, decimal? ThresholdPercent = null) : IQuery<PeriodComparisonDto>;

public sealed class GetPeriodComparisonHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<GetPeriodComparisonQuery, PeriodComparisonDto>
{
    public async Task<Result<PeriodComparisonDto>> Handle(GetPeriodComparisonQuery query, CancellationToken cancellationToken)
    {
        var threshold = query.ThresholdPercent ?? PeriodComparisonCalculator.DefaultThresholdPercent;
        if (threshold < 0m)
        {
            return Error.Validation(PayrollDomainErrors.ComparisonThresholdInvalid);
        }

        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var (previousYear, previousMonth) = period.Month == 1 ? (period.Year - 1, 12) : (period.Year, period.Month - 1);
        var previousPeriod = await periods.FindByYearMonthAsync(previousYear, previousMonth, cancellationToken);

        var current = Figures(await payslips.ListByPeriodAsync(period.Id, cancellationToken));
        var previous = previousPeriod is null ? [] : Figures(await payslips.ListByPeriodAsync(previousPeriod.Id, cancellationToken));
        var comparison = PeriodComparisonCalculator.Compare(current, previous, threshold);

        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, comparison.Rows.Select(r => r.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);
        var lines = comparison.Rows.Select(r =>
        {
            var contact = contacts.GetValueOrDefault(r.EmployeeId);
            var name = contact is null ? r.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            return new ComparisonLineDto(
                r.EmployeeId, name, r.Kind, r.CurrentGross, r.PreviousGross, r.CurrentNet, r.PreviousNet, r.GrossChangePercent, r.NetChangePercent, r.ExceedsThreshold);
        }).ToList();

        return new PeriodComparisonDto(
            period.Id, previousPeriod?.Id, previousYear, previousMonth, threshold, comparison.Current, comparison.Previous,
            lines.Count(l => l.ExceedsThreshold), lines);
    }

    private static List<PayslipFigures> Figures(IReadOnlyList<Domain.Payslips.Payslip> list) =>
        list.Select(p => new PayslipFigures(p.EmployeeId, p.GrossAmount, p.NetAmount, p.EmployerCost)).ToList();
}
