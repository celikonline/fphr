using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.Employees;

/// <summary>
/// Downloads a terminated employee's çalışma belgesi (4857 md.28) as PDF - the same document the exit e-mail attaches.
/// Needs employee.employees.update (the permission that terminates): the document carries the national id.
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesUpdateKey)]
public sealed record GetWorkCertificateQuery(Guid EmployeeId) : IQuery<ExportFileResult>;

public sealed class GetWorkCertificateHandler(ITenantContext tenant, IWorkCertificateProvider certificates)
    : IQueryHandler<GetWorkCertificateQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetWorkCertificateQuery query, CancellationToken cancellationToken)
    {
        var file = await certificates.RenderPdfAsync(tenant.TenantId, query.EmployeeId, cancellationToken);
        return file is null ? Error.NotFound(EmployeeDomainErrors.EmployeeNotFound) : file;
    }
}
