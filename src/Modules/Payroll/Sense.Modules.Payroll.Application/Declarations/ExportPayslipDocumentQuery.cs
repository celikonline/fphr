using System.Text;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>Bir çalışanın bir dönemdeki ücret pusulasını (senseik-019 3a, m.37) düz metin olarak indirir.
/// payroll.trace.view gerektirir (diğer salt-okunur bordro görüntüleme uç noktalarıyla aynı izin).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ExportPayslipDocumentQuery(Guid PayslipId) : IQuery<ExportFileResult>;

public sealed class ExportPayslipDocumentHandler(
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IPayrollPeriodRepository periods,
    IEmployeeDirectory employeeDirectory,
    ITenantContext tenant) : IQueryHandler<ExportPayslipDocumentQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportPayslipDocumentQuery query, CancellationToken cancellationToken)
    {
        var payslip = await payslips.FindByIdAsync(query.PayslipId, cancellationToken);
        if (payslip is null)
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        var period = await periods.FindByIdAsync(payslip.PayrollPeriodId, cancellationToken);
        if (period is null)
        {
            return Error.NotFound(PayrollDomainErrors.PeriodNotFound);
        }

        var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
        var steps = PayslipTraceSteps.Parse(trace?.StepsJson);

        var contacts = await employeeDirectory.GetContactsAsync(tenant.TenantId, [payslip.EmployeeId], cancellationToken);
        var contact = contacts.FirstOrDefault();
        var employeeName = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();

        var content = PayslipDocumentBuilder.BuildText(employeeName, period.Year, period.Month, steps, payslip.GrossAmount, payslip.NetAmount);
        var fileName = $"ucret-pusulasi_{period.Year}-{period.Month:D2}_{payslip.EmployeeId}.txt";
        return new ExportFileResult(fileName, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(content));
    }
}
