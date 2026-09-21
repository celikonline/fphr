using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>One payslip of an employee with its period, as the "Bordro" tab of the employee card lists it.</summary>
public sealed record EmployeePayslipDto(
    Guid PayslipId,
    Guid PeriodId,
    int Year,
    int Month,
    PayrollPeriodStatus PeriodStatus,
    decimal GrossAmount,
    decimal NetAmount,
    decimal EmployerCost);

/// <summary>Every payslip of the employee, newest period first (same read-only gate as the period detail).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListEmployeePayslipsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<EmployeePayslipDto>>;

public sealed class ListEmployeePayslipsHandler(IPayslipRepository payslips, IPayrollPeriodRepository periods)
    : IQueryHandler<ListEmployeePayslipsQuery, IReadOnlyList<EmployeePayslipDto>>
{
    public async Task<Result<IReadOnlyList<EmployeePayslipDto>>> Handle(ListEmployeePayslipsQuery query, CancellationToken cancellationToken)
    {
        var mine = await payslips.ListByEmployeeAsync(query.EmployeeId, cancellationToken);
        if (mine.Count == 0)
        {
            return Result.Success<IReadOnlyList<EmployeePayslipDto>>([]);
        }

        var periodsById = (await periods.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        IReadOnlyList<EmployeePayslipDto> list = mine
            .Where(p => periodsById.ContainsKey(p.PayrollPeriodId))
            .Select(p => (Payslip: p, Period: periodsById[p.PayrollPeriodId]))
            .OrderByDescending(x => x.Period.Year)
            .ThenByDescending(x => x.Period.Month)
            .Select(x => new EmployeePayslipDto(
                x.Payslip.Id, x.Period.Id, x.Period.Year, x.Period.Month, x.Period.Status,
                x.Payslip.GrossAmount, x.Payslip.NetAmount, x.Payslip.EmployerCost))
            .ToList();
        return Result.Success(list);
    }
}
