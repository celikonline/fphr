using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;

namespace Sense.Modules.Employee.Application.Career;

/// <summary>
/// Snapshot of the tenant's active hierarchy (levels + units, read through <see cref="IOrgUnitLookup"/>) used by every
/// career write: level order for the "deepest unit" denormalization, validation that a unit really belongs to the
/// level it is sent for, and the unit chain a single org unit id expands to.
/// </summary>
public sealed class OrgPlacement
{
    public OrgPlacement(IReadOnlyList<OrgUnitTypeInfo> types, IReadOnlyList<OrgUnitInfo> units)
    {
        Types = types.OrderBy(t => t.SortOrder).ToList();
        LevelOrder = Types.ToDictionary(t => t.Id, t => t.SortOrder);
        UnitsById = units.ToDictionary(u => u.Id);
    }

    public IReadOnlyList<OrgUnitTypeInfo> Types { get; }

    public IReadOnlyDictionary<Guid, int> LevelOrder { get; }

    public IReadOnlyDictionary<Guid, OrgUnitInfo> UnitsById { get; }

    /// <summary>True when the unit is active and belongs to the given level.</summary>
    public bool IsValidAssignment(Guid orgUnitTypeId, Guid orgUnitId) =>
        LevelOrder.ContainsKey(orgUnitTypeId) && UnitsById.TryGetValue(orgUnitId, out var unit) && unit.TypeId == orgUnitTypeId;

    /// <summary>
    /// The per-level assignments a single "deepest unit" stands for: the unit itself plus every ancestor that sits on
    /// a shallower level not already taken, so the deepest assignment is always the unit that was asked for.
    /// </summary>
    public IReadOnlyList<OrgAssignmentInput> ChainFor(Guid? orgUnitId)
    {
        if (orgUnitId is not { } id || !UnitsById.TryGetValue(id, out var unit))
        {
            return [];
        }

        var deepestOrder = LevelOrder.TryGetValue(unit.TypeId, out var order) ? order : int.MaxValue;
        var chain = new List<OrgAssignmentInput> { new(unit.TypeId, unit.Id) };
        var visited = new HashSet<Guid> { unit.Id };
        var parentId = unit.ParentId;
        while (parentId is { } current && visited.Add(current) && UnitsById.TryGetValue(current, out var parent))
        {
            if (LevelOrder.TryGetValue(parent.TypeId, out var parentOrder) && parentOrder < deepestOrder &&
                chain.All(a => a.OrgUnitTypeId != parent.TypeId))
            {
                chain.Add(new OrgAssignmentInput(parent.TypeId, parent.Id));
            }

            parentId = parent.ParentId;
        }

        return chain;
    }

    /// <summary>The chain unit at the level with <paramref name="levelCode"/> (e.g. "Department", "Title"), or null when the chain has none.</summary>
    public OrgUnitInfo? LevelUnitFor(Guid? orgUnitId, string levelCode)
    {
        var level = Types.FirstOrDefault(t => t.Code == levelCode);
        var assignment = level is null ? null : ChainFor(orgUnitId).FirstOrDefault(a => a.OrgUnitTypeId == level.Id);
        return assignment is null ? null : UnitsById.GetValueOrDefault(assignment.OrgUnitId);
    }
}

/// <summary>Loads the <see cref="OrgPlacement"/> snapshot once per request (or per import batch).</summary>
public sealed class OrgPlacementLoader(ITenantContext tenant, IOrgUnitLookup orgUnits)
{
    public async Task<OrgPlacement> LoadAsync(CancellationToken ct)
    {
        var types = await orgUnits.ListActiveTypesAsync(tenant.TenantId, ct).ConfigureAwait(false);
        var units = await orgUnits.ListActiveUnitsAsync(tenant.TenantId, ct).ConfigureAwait(false);
        return new OrgPlacement(types, units);
    }
}
