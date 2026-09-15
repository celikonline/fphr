using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Application.Locations;

/// <summary>
/// Grid-standard (page/pageSize/sort/q) list of locations for the current tenant, matching the shape
/// web/src/pages/locations.tsx already calls (see web/src/services/locations.service.ts). Filtering/
/// sorting/paging is done in-memory over the (small, per-tenant) active location set rather than via a
/// dedicated EF read-store - Organization's other list endpoints (see OrgUnitQueries) follow the same
/// "flat repository list + in-process shaping" style rather than Identity's IIdentityReadStore pattern.
/// </summary>
[RequiresPermission("organization.locations.read")]
public sealed record ListLocationsQuery(PagedQuery Paging) : IQuery<PagedResult<LocationDto>>;

public sealed class ListLocationsHandler(ILocationRepository locations) : IQueryHandler<ListLocationsQuery, PagedResult<LocationDto>>
{
    public async Task<Result<PagedResult<LocationDto>>> Handle(ListLocationsQuery query, CancellationToken cancellationToken)
    {
        var all = await locations.ListAllAsync(activeOnly: true, cancellationToken);

        IEnumerable<Location> filtered = all;
        var q = query.Paging.Q;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            filtered = filtered.Where(l =>
                l.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                (l.Address is not null && l.Address.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }

        var sorted = Sort(filtered, query.Paging.SortClauses).ToList();

        var page = query.Paging.Page;
        var pageSize = query.Paging.PageSize;
        var items = sorted.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto).ToList();

        return Result.Success(new PagedResult<LocationDto>(items, page, pageSize, sorted.Count));
    }

    private static IEnumerable<Location> Sort(IEnumerable<Location> source, IReadOnlyList<SortClause> clauses)
    {
        if (clauses.Count == 0)
        {
            return source.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase);
        }

        IOrderedEnumerable<Location>? ordered = null;
        foreach (var clause in clauses)
        {
            Func<Location, object> key = clause.Field.ToLowerInvariant() switch
            {
                "name" => l => l.Name,
                "type" => l => l.Type,
                "countrycode" => l => l.CountryCode,
                _ => l => l.Name,
            };

            ordered = ordered is null
                ? (clause.Descending ? source.OrderByDescending(key) : source.OrderBy(key))
                : (clause.Descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        }

        return ordered ?? source;
    }

    internal static LocationDto ToDto(Location l) => new(l.Id, l.Name, l.Type, l.CountryCode, l.Address, l.IsActive);
}
