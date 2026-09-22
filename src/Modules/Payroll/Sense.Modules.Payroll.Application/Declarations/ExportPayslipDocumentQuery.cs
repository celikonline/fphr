using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Payroll.Application.Periods;
using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>Bir çalışanın bir dönemdeki ücret pusulasını (senseik-019 3a, m.37) PDF olarak indirir.
/// payroll.trace.view gerektirir (diğer salt-okunur bordro görüntüleme uç noktalarıyla aynı izin).</summary>
[RequiresPermission("payroll.trace.view")]
public sealed record ExportPayslipDocumentQuery(Guid PayslipId) : IQuery<ExportFileResult>;

public sealed class ExportPayslipDocumentHandler(IPayslipDocumentProvider documents, ITenantContext tenant)
    : IQueryHandler<ExportPayslipDocumentQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(ExportPayslipDocumentQuery query, CancellationToken cancellationToken)
    {
        var file = await documents.RenderPdfAsync(tenant.TenantId, query.PayslipId, cancellationToken);
        return file is null ? Error.NotFound(PayrollDomainErrors.PayslipNotFound) : file;
    }
}

/// <summary>
/// <see cref="IPayslipDocumentProvider"/>: the payslip's latest calculation trace laid out by
/// <see cref="PayslipDocumentBuilder.BuildPdf"/>; the issuer is the Company-level org unit of the employee. The payslip
/// must belong to the given tenant - a mismatch (e.g. a caller without the right ambient tenant scope) renders nothing.
/// </summary>
public sealed class PayslipDocumentProvider(
    IPayslipRepository payslips,
    IPayslipCalculationTraceRepository traces,
    IPayrollPeriodRepository periods,
    IEmployeeContactLookup contacts,
    IEmployeeLookup employees,
    IOrgUnitLookup orgUnits,
    ISimplePdfWriter pdfWriter) : IPayslipDocumentProvider
{
    private const string CompanyLevelCode = "Company";

    public async Task<ExportFileResult?> RenderPdfAsync(Guid tenantId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var payslip = await payslips.FindByIdAsync(payslipId, cancellationToken);
        var period = payslip is null || payslip.TenantId != tenantId
            ? null
            : await periods.FindByIdAsync(payslip.PayrollPeriodId, cancellationToken);
        if (payslip is null || period is null || period.TenantId != tenantId)
        {
            return null;
        }

        var trace = await traces.FindLatestByPayslipIdAsync(payslip.Id, cancellationToken);
        var steps = PayslipTraceSteps.Parse(trace?.StepsJson);

        var contact = (await contacts.GetContactsAsync(tenantId, [payslip.EmployeeId], cancellationToken)).FirstOrDefault();
        var employeeName = contact is null ? payslip.EmployeeId.ToString() : $"{contact.FirstName} {contact.LastName}".Trim();
        var issuer = await CompanyNameAsync(tenantId, payslip.EmployeeId, cancellationToken);

        var document = PayslipDocumentBuilder.BuildPdf(employeeName, issuer, period.Year, period.Month, steps, payslip.GrossAmount, payslip.NetAmount);
        var fileName = $"ucret-pusulasi_{period.Year}-{period.Month:D2}_{payslip.EmployeeId}.pdf";
        return new ExportFileResult(fileName, "application/pdf", pdfWriter.Write(document));
    }

    private async Task<string?> CompanyNameAsync(Guid tenantId, Guid employeeId, CancellationToken ct)
    {
        if (await employees.GetOrgUnitIdAsync(tenantId, employeeId, ct) is not { } orgUnitId)
        {
            return null;
        }

        var types = await orgUnits.ListActiveTypesAsync(tenantId, ct);
        var companyType = types.FirstOrDefault(t => t.Code == CompanyLevelCode);
        if (companyType is null)
        {
            return null;
        }

        var units = (await orgUnits.ListActiveUnitsAsync(tenantId, ct)).ToDictionary(u => u.Id);
        var visited = new HashSet<Guid>();
        for (Guid? current = orgUnitId; current is { } id && visited.Add(id) && units.TryGetValue(id, out var unit); current = unit.ParentId)
        {
            if (unit.TypeId == companyType.Id)
            {
                return unit.Name;
            }
        }

        return null;
    }
}
