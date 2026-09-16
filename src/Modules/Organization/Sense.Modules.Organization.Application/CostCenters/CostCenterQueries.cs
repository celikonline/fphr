using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.CostCenters;

/// <summary>List of cost centers for the current tenant, sorted by code (S-4, roadmap Faz 2 item #2). Flat list - no paging, mirrors the sözleşme (<c>CostCenterDto[]</c>) FE consumes.</summary>
[RequiresPermission(OrganizationPermissions.CostCentersViewKey)]
public sealed record ListCostCentersQuery(bool IncludeInactive) : IQuery<IReadOnlyList<CostCenterDto>>;

public sealed class ListCostCentersHandler(ICostCenterRepository costCenters) : IQueryHandler<ListCostCentersQuery, IReadOnlyList<CostCenterDto>>
{
    public async Task<Result<IReadOnlyList<CostCenterDto>>> Handle(ListCostCentersQuery query, CancellationToken cancellationToken)
    {
        var all = await costCenters.ListAllAsync(activeOnly: !query.IncludeInactive, cancellationToken);
        IReadOnlyList<CostCenterDto> items = [.. all.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(CostCenterMapper.ToDto)];
        return Result.Success(items);
    }
}
