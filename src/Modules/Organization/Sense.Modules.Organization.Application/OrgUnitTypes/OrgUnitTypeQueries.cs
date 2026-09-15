using Sense.Modules.Organization.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.OrgUnitTypes;

/// <summary>Lists hierarchy levels (types) for the current tenant, ordered by SortOrder.</summary>
[RequiresPermission("organization.units.read")]
public sealed record ListOrgUnitTypesQuery(bool ActiveOnly = false) : IQuery<IReadOnlyList<OrgUnitTypeDto>>;

public sealed class ListOrgUnitTypesHandler(IOrgUnitTypeRepository types) : IQueryHandler<ListOrgUnitTypesQuery, IReadOnlyList<OrgUnitTypeDto>>
{
    public async Task<Result<IReadOnlyList<OrgUnitTypeDto>>> Handle(ListOrgUnitTypesQuery query, CancellationToken cancellationToken)
    {
        var list = await types.ListAsync(query.ActiveOnly, cancellationToken);

        IReadOnlyList<OrgUnitTypeDto> dtos = list
            .OrderBy(t => t.SortOrder)
            .Select(t => new OrgUnitTypeDto(t.Id, t.Code, t.Name, t.SortOrder, t.IsActive, t.IsSystem, t.AllowsNesting))
            .ToList();

        // Result<TValue>'s implicit conversion operator is not applicable when TValue is an interface
        // type (C# excludes interface source/target types from user-defined conversions), so an
        // IReadOnlyList<T> result must go through Result.Success(...) explicitly rather than `return dtos;`.
        return Result.Success(dtos);
    }
}
