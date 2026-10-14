namespace Sense.Modules.Organization.Application.OrgUnits;

/// <summary>
/// Builds a parent/child tree from a flat, already-loaded list of org units.
///
/// Chosen over a recursive SQL CTE: a tenant's org unit count is small (tens to low hundreds - one
/// row per hierarchy node, not per employee), so a single flat query plus an in-memory tree build is
/// simpler to unit-test, keeps the codebase's read paths as plain LINQ (no raw-SQL/CTE escape hatch),
/// and is O(n) with a dictionary grouped by ParentId. Revisit with a recursive CTE only if a tenant's
/// org tree grows into the thousands of units.
/// </summary>
public static class OrgUnitTreeBuilder
{
    /// <summary>Sentinel key standing in for "no parent" (root level) - Dictionary&lt;TKey,...&gt; requires a non-nullable key, and Guid.Empty is never a real OrgUnit.Id (see Guid.CreateVersion7 usage in OrgUnit.Create).</summary>
    private static readonly Guid RootKey = Guid.Empty;

    public static IReadOnlyList<OrgUnitTreeNodeDto> Build(IEnumerable<OrgUnitDto> units)
    {
        var byParent = units
            .GroupBy(u => u.ParentId ?? RootKey)
            .ToDictionary(g => g.Key, g => g.OrderBy(u => u.SortOrder).ToList());

        return BuildLevel(RootKey, byParent, []);
    }

    private static List<OrgUnitTreeNodeDto> BuildLevel(Guid parentKey, Dictionary<Guid, List<OrgUnitDto>> byParent, HashSet<Guid> visited)
    {
        if (!byParent.TryGetValue(parentKey, out var children))
        {
            return [];
        }

        var result = new List<OrgUnitTreeNodeDto>();
        foreach (var unit in children)
        {
            // Guards against a corrupt/cyclic ParentId chain (should never happen if writes are
            // validated, but a read path must not be able to recurse forever if one slips through).
            if (!visited.Add(unit.Id))
            {
                continue;
            }

            result.Add(new OrgUnitTreeNodeDto(
                unit.Id,
                unit.TypeId,
                unit.Name,
                unit.ManagerEmployeeId,
                unit.LocationId,
                unit.SortOrder,
                unit.IsActive,
                BuildLevel(unit.Id, byParent, visited),
                unit.CostCenterId));
        }

        return result;
    }
}
