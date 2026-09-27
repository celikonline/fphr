using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Periods;

/// <summary>One of the caller's own payslips ("Bordro Pusulam", TK-030). No employer cost - that figure is not the
/// employee's business, unlike the HR-facing <see cref="EmployeePayslipDto"/>. <see cref="HasDocument"/> is always
/// true for a listed row: <c>GET payroll/payslips/my/{id}/document</c> can always render a persisted payslip's PDF.</summary>
public sealed record MyPayslipDto(Guid PayslipId, int Year, int Month, decimal GrossAmount, decimal NetAmount, bool HasDocument, DateTime CreatedAt);

/// <summary>The caller's own payslips, newest period first - only for periods already Paid or Locked
/// (<see cref="PayslipVisibility"/>): an Open/Approved period may still change before payment.</summary>
[RequiresPermission(PayrollPermissions.MyPayslipsKey)]
public sealed record GetMyPayslipsQuery : IQuery<IReadOnlyList<MyPayslipDto>>;

public sealed class GetMyPayslipsHandler(IPayslipRepository payslips, IPayrollPeriodRepository periods, ICurrentUser currentUser)
    : IQueryHandler<GetMyPayslipsQuery, IReadOnlyList<MyPayslipDto>>
{
    public async Task<Result<IReadOnlyList<MyPayslipDto>>> Handle(GetMyPayslipsQuery query, CancellationToken cancellationToken)
    {
        // Çalışan kaydı olmayan kullanıcının (ör. yalnız yönetici hesabı) bordro pusulası olamaz.
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Result.Success<IReadOnlyList<MyPayslipDto>>([]);
        }

        var mine = await payslips.ListByEmployeeAsync(employeeId, cancellationToken);
        if (mine.Count == 0)
        {
            return Result.Success<IReadOnlyList<MyPayslipDto>>([]);
        }

        var periodsById = (await periods.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        IReadOnlyList<MyPayslipDto> list = mine
            .Where(p => periodsById.TryGetValue(p.PayrollPeriodId, out var period) && PayslipVisibility.IsVisibleToEmployee(period.Status))
            .Select(p => (Payslip: p, Period: periodsById[p.PayrollPeriodId]))
            .OrderByDescending(x => x.Period.Year)
            .ThenByDescending(x => x.Period.Month)
            .Select(x => new MyPayslipDto(x.Payslip.Id, x.Period.Year, x.Period.Month, x.Payslip.GrossAmount, x.Payslip.NetAmount, HasDocument: true, x.Payslip.CreatedAt))
            .ToList();
        return Result.Success(list);
    }
}
