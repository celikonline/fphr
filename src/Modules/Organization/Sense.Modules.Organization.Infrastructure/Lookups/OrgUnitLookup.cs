using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Implements Organization.Contracts.IOrgUnitLookup so other modules (e.g. Employee) can validate an OrgUnitId reference.</summary>
public sealed class OrgUnitLookup(OrganizationDbContext context) : IOrgUnitLookup
{
    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.TenantId == tenantId && u.Id == orgUnitId && u.IsActive, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetSelfAndDescendantIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken ct = default)
    {
        if (orgUnitIds.Count == 0)
        {
            return [];
        }

        var units = await context.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Select(u => new { u.Id, u.ParentId })
            .ToListAsync(ct);

        var childrenByParent = units.Where(u => u.ParentId is not null).ToLookup(u => u.ParentId!.Value, u => u.Id);
        var activeIds = units.Select(u => u.Id).ToHashSet();

        var result = new HashSet<Guid>();
        var pending = new Queue<Guid>(orgUnitIds.Where(activeIds.Contains));
        while (pending.TryDequeue(out var id))
        {
            if (!result.Add(id))
            {
                continue;
            }

            foreach (var child in childrenByParent[id])
            {
                pending.Enqueue(child);
            }
        }

        return result.ToList();
    }

    public async Task<IReadOnlyList<OrgUnitTypeInfo>> ListActiveTypesAsync(Guid tenantId, CancellationToken ct = default) =>
        await context.OrgUnitTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.SortOrder)
            .Select(t => new OrgUnitTypeInfo(t.Id, t.Code, t.Name, t.SortOrder))
            .ToListAsync(ct);

    public async Task<Guid?> GetEffectiveLocationIdAsync(Guid tenantId, Guid orgUnitId, CancellationToken ct = default)
    {
        var units = await context.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Id, u.ParentId, u.LocationId })
            .ToDictionaryAsync(u => u.Id, ct);
        var visited = new HashSet<Guid>();
        for (Guid? current = orgUnitId; current is { } id && units.TryGetValue(id, out var unit) && visited.Add(id); current = unit.ParentId)
        {
            if (unit.LocationId is { } locationId)
            {
                return locationId;
            }
        }

        return null;
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetEffectiveLocationMapAsync(Guid tenantId, CancellationToken ct = default)
    {
        var units = await context.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Id, u.ParentId, u.LocationId })
            .ToDictionaryAsync(u => u.Id, ct);
        var map = new Dictionary<Guid, Guid>();
        foreach (var start in units.Keys)
        {
            var visited = new HashSet<Guid>();
            for (Guid? current = start; current is { } id && units.TryGetValue(id, out var unit) && visited.Add(id); current = unit.ParentId)
            {
                if (unit.LocationId is { } locationId)
                {
                    map[start] = locationId;
                    break;
                }
            }
        }

        return map;
    }

    public async Task<IReadOnlyList<OrgUnitInfo>> ListActiveUnitsAsync(Guid tenantId, CancellationToken ct = default) =>
        await context.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .OrderBy(u => u.SortOrder).ThenBy(u => u.Name)
            .Select(u => new OrgUnitInfo(u.Id, u.TypeId, u.ParentId, u.Name))
            .ToListAsync(ct);
}
