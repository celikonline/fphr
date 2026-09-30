using Sense.Modules.Employee.Application.Payments;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.CardPdf;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Application.CardPdf;

/// <summary>
/// Employee card as PDF (selected tabs + period). Scoped on employees.read, which also guards section queries that are
/// not scoped themselves (assets, payroll); every section still needs its own permission.
/// </summary>
[RequiresPermission(EmployeePermissions.EmployeesReadKey)]
public sealed record GetEmployeeCardPdfQuery(Guid Id, IReadOnlyList<string> Sections, DateOnly? From, DateOnly? To)
    : IQuery<ExportFileResult>, IScopedRequest
{
    public Guid? TargetEmployeeId => Id;
}

public sealed class GetEmployeeCardPdfHandler(ITenantContext tenant, IEmployeeReadStore store, EmployeeCardPdfComposer composer)
    : IQueryHandler<GetEmployeeCardPdfQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetEmployeeCardPdfQuery query, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(query.Id, cancellationToken);
        if (row is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        var request = new EmployeeCardPdfRequest(tenant.TenantId, row.Id, IsOwnCard: false, query.From, query.To);
        return await composer.ComposeAsync(request, row.DisplayName, query.Sections, cancellationToken);
    }
}

/// <summary>The caller's own card as PDF (self-service); sections use the modules' "my" queries.</summary>
[RequiresPermission(EmployeePermissions.MyViewKey)]
public sealed record GetMyEmployeeCardPdfQuery(IReadOnlyList<string> Sections, DateOnly? From, DateOnly? To) : IQuery<ExportFileResult>;

public sealed class GetMyEmployeeCardPdfHandler(
    ITenantContext tenant, SelfEmployeeResolver self, IEmployeeReadStore store, EmployeeCardPdfComposer composer)
    : IQueryHandler<GetMyEmployeeCardPdfQuery, ExportFileResult>
{
    public async Task<Result<ExportFileResult>> Handle(GetMyEmployeeCardPdfQuery query, CancellationToken cancellationToken)
    {
        var row = await self.ResolveAsync(cancellationToken) is { } id ? await store.GetAsync(id, cancellationToken) : null;
        if (row is null)
        {
            return Error.NotFound(EmployeeErrors.EmployeeNotFound);
        }

        var request = new EmployeeCardPdfRequest(tenant.TenantId, row.Id, IsOwnCard: true, query.From, query.To);
        return await composer.ComposeAsync(request, row.DisplayName, query.Sections, cancellationToken);
    }
}
