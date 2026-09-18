namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Pure, in-memory view over a tenant's active hierarchy levels and units (as returned by
/// <see cref="IOrgUnitLookup.ListActiveTypesAsync"/> / <see cref="IOrgUnitLookup.ListActiveUnitsAsync"/>) for callers
/// that render "kariyer bilgileri şirket yapınıza göre dallanır" columns (Kolay İK reports: one column per level -
/// Şirket / Şube / Departman / Unvan…, 11.3.12). Given an employee's placement it resolves the unit name on every
/// level: explicit per-level assignments win, then the ancestors of the deepest known unit fill the rest.
/// </summary>
public sealed class OrgUnitHierarchy
{
    private readonly IReadOnlyDictionary<Guid, OrgUnitInfo> _unitsById;

    public OrgUnitHierarchy(IReadOnlyList<OrgUnitTypeInfo> types, IReadOnlyList<OrgUnitInfo> units)
    {
        Levels = types.OrderBy(t => t.SortOrder).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        _unitsById = units.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Active levels ordered by SortOrder (Company first) - the report's dynamic column order.</summary>
    public IReadOnlyList<OrgUnitTypeInfo> Levels { get; }

    public string? UnitName(Guid? unitId) =>
        unitId is { } id && _unitsById.TryGetValue(id, out var unit) ? unit.Name : null;

    /// <summary>
    /// Level id → unit name for one placement. <paramref name="assignments"/> is the employee's current per-level
    /// assignment (OrgUnitTypeId → OrgUnitId, may be empty), <paramref name="deepestUnitId"/> the denormalized unit
    /// (Employee.OrgUnitId). Levels neither assigned nor reachable through the parent chain are absent.
    /// </summary>
    public IReadOnlyDictionary<Guid, string> ResolveLevelNames(IReadOnlyDictionary<Guid, Guid>? assignments, Guid? deepestUnitId)
    {
        var result = new Dictionary<Guid, string>();
        if (assignments is not null)
        {
            foreach (var (typeId, unitId) in assignments)
            {
                if (_unitsById.TryGetValue(unitId, out var unit))
                {
                    result[typeId] = unit.Name;
                }
            }
        }

        // Walk up from the deepest unit so a placement stored only as the leaf still shows its company/branch.
        var start = deepestUnitId ?? assignments?.Values.Select(id => (Guid?)id).FirstOrDefault(id => id is { } v && _unitsById.ContainsKey(v));
        var visited = new HashSet<Guid>();
        var current = start;
        while (current is { } id && visited.Add(id) && _unitsById.TryGetValue(id, out var unit))
        {
            result.TryAdd(unit.TypeId, unit.Name);
            current = unit.ParentId;
        }

        return result;
    }

    /// <summary>Convenience for report rows: the resolved names in <see cref="Levels"/> order (null where unknown).</summary>
    public IReadOnlyList<string?> LevelValues(IReadOnlyDictionary<Guid, Guid>? assignments, Guid? deepestUnitId)
    {
        var names = ResolveLevelNames(assignments, deepestUnitId);
        return Levels.Select(l => names.TryGetValue(l.Id, out var name) ? name : null).ToList();
    }
}
