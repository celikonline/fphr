using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.OrgUnits;

/// <summary>Flat, tenant-scoped list of org units (client can build its own tree, or call GetOrgUnitTreeQuery).</summary>
[RequiresPermission("organization.units.read")]
public sealed record ListOrgUnitsQuery(bool ActiveOnly = true) : IQuery<IReadOnlyList<OrgUnitDto>>;

public sealed class ListOrgUnitsHandler(IOrgUnitRepository units) : IQueryHandler<ListOrgUnitsQuery, IReadOnlyList<OrgUnitDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitDto>>> Handle(ListOrgUnitsQuery query, CancellationToken cancellationToken)
    {
        var list = await units.ListAllAsync(query.ActiveOnly, cancellationToken);
        IReadOnlyList<OrgUnitDto> dtos = list.Select(ToDto).ToList();

        // Result<TValue>'s implicit conversion is not applicable when TValue is an interface type, so
        // an IReadOnlyList<T> result must go through Result.Success(...) explicitly.
        return Result.Success(dtos);
    }

    internal static OrgUnitDto ToDto(OrgUnit u) =>
        new(u.Id, u.TypeId, u.ParentId, u.Name, u.ManagerEmployeeId, u.LocationId, u.SortOrder, u.IsActive, u.CostCenterId);
}

/// <summary>Full org unit tree for the current tenant, server-built from the flat active list (see OrgUnitTreeBuilder).</summary>
[RequiresPermission("organization.units.read")]
public sealed record GetOrgUnitTreeQuery : IQuery<IReadOnlyList<OrgUnitTreeNodeDto>>;

public sealed class GetOrgUnitTreeHandler(IOrgUnitRepository units) : IQueryHandler<GetOrgUnitTreeQuery, IReadOnlyList<OrgUnitTreeNodeDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitTreeNodeDto>>> Handle(GetOrgUnitTreeQuery query, CancellationToken cancellationToken)
    {
        var list = await units.ListAllAsync(activeOnly: true, cancellationToken);
        var dtos = list.Select(ListOrgUnitsHandler.ToDto);
        return Result.Success(OrgUnitTreeBuilder.Build(dtos));
    }
}
