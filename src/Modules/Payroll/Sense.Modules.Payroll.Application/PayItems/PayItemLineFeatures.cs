using System.Globalization;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.PayItems;

/// <summary>One pay item line of an employee's latest calculation in a period (read from the PAY_ITEM trace steps).</summary>
/// <param name="AmountBasis">"gross" or "net".</param>
/// <param name="NetTarget">The net amount the employee keeps; only for net items.</param>
public sealed record PayItemLineDto(Guid EmployeeId, string EmployeeName, string Code, string Name, decimal Amount, string AmountBasis, decimal? NetTarget = null);

/// <summary>The pay-item lines of every payslip in the period, from each payslip's latest calculation trace ("Dönem dökümü").</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ListPeriodPayItemLinesQuery(Guid PeriodId) : IQuery<IReadOnlyList<PayItemLineDto>>;

public sealed class ListPeriodPayItemLinesHandler(
    IPayrollPeriodRepository periods,
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<ListPeriodPayItemLinesQuery, IReadOnlyList<PayItemLineDto>>
{
    public async Task<Result<IReadOnlyList<PayItemLineDto>>> Handle(ListPeriodPayItemLinesQuery query, CancellationToken cancellationToken)
    {
        var period = await periods.FindByIdAsync(query.PeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var periodPayslips = await payslips.ListByPeriodAsync(period.Id, cancellationToken);
        var contacts = (await employeeDirectory.GetContactsAsync(tenant.TenantId, periodPayslips.Select(p => p.EmployeeId).ToList(), cancellationToken))
            .ToDictionary(c => c.EmployeeId);

        var lines = new List<PayItemLineDto>();
        foreach (var payslip in periodPayslips)
        {
            var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
            var payItemSteps = PayslipTraceSteps.Parse(trace?.StepsJson).Where(s => s.Code == PayrollTraceStepCodes.PayItem).ToList();
            if (payItemSteps.Count == 0)
            {
                continue;
            }

            var contact = contacts.GetValueOrDefault(payslip.EmployeeId);
            var employeeName = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
            lines.AddRange(payItemSteps.Select(step => ToLine(payslip.EmployeeId, employeeName, step)));
        }

        return lines;
    }

    private static PayItemLineDto ToLine(Guid employeeId, string employeeName, TraceStep step)
    {
        var code = step.PayItem ?? step.Inputs.GetValueOrDefault("code") ?? string.Empty;
        var name = step.Inputs.GetValueOrDefault("name") ?? code;
        var basis = step.Inputs.GetValueOrDefault("amountBasis") ?? "gross";
        decimal? netTarget = step.Inputs.TryGetValue("netTarget", out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var net) ? net : null;
        return new PayItemLineDto(employeeId, employeeName, code, name, step.Output, basis, netTarget);
    }
}
