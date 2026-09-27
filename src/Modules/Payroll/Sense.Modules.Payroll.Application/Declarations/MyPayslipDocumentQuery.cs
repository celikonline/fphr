using Sense.Modules.Payroll.Contracts;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>Kendi ücret pusulasının PDF'i (TK-030). Başkasının pusulası, görünmeyen (Open/Approved) bir dönem ya da
/// tenant uyuşmazlığı - hepsi aynı "bulunamadı" hatasını döner; varlığı sızdırmaz.</summary>
[RequiresPermission(PayrollPermissions.MyPayslipsKey)]
public sealed record GetMyPayslipDocumentQuery(Guid PayslipId) : IQuery<ExportFileResult>;

public sealed class GetMyPayslipDocumentHandler(
    IPayslipRepository payslips, IPayrollPeriodRepository periods, IPayslipDocumentProvider documents,
    ITenantContext tenant, ICurrentUser currentUser)
    : IQueryHandler<GetMyPayslipDocumentQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetMyPayslipDocumentQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        var payslip = await payslips.FindByIdAsync(query.PayslipId, cancellationToken);
        var period = payslip is null ? null : await periods.FindByIdAsync(payslip.PayrollPeriodId, cancellationToken);
        if (payslip is null || payslip.EmployeeId != employeeId || period is null || !PayslipVisibility.IsVisibleToEmployee(period.Status))
        {
            return Error.NotFound(PayrollDomainErrors.PayslipNotFound);
        }

        var file = await documents.RenderPdfAsync(tenant.TenantId, query.PayslipId, cancellationToken);
        return file is null ? Error.NotFound(PayrollDomainErrors.PayslipNotFound) : file;
    }
}
