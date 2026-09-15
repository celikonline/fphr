namespace Sense.Modules.Organization.Domain.ApprovalPolicies;

/// <summary>Minimal org unit shape needed to walk the hierarchy.</summary>
public readonly record struct OrgUnitNode(Guid Id, Guid? ParentId, bool IsActive);

/// <summary>Effective assignment for a request type and the unit it comes from.</summary>
public sealed record EffectiveApprovalPolicy(Guid ApprovalPolicyId, Guid SourceOrgUnitId);

/// <summary>
/// Unit-based approval policy inheritance (11.3.04 "alt birimde atama yoksa üstünkini devralır"): the unit itself, then
/// each parent up to the root; inactive units are walked through but their assignments are ignored; first match wins.
/// </summary>
public static class ApprovalPolicyInheritance
{
    /// <summary>Active units on the path from <paramref name="orgUnitId"/> to the root, nearest first. Empty for an unknown unit.</summary>
    public static IReadOnlyList<Guid> ActivePath(Guid orgUnitId, IReadOnlyDictionary<Guid, OrgUnitNode> units)
    {
        var path = new List<Guid>();
        var visited = new HashSet<Guid>();
        Guid? current = orgUnitId;
        while (current is { } id && visited.Add(id) && units.TryGetValue(id, out var node))
        {
            if (node.IsActive)
            {
                path.Add(id);
            }

            current = node.ParentId;
        }

        return path;
    }

    /// <summary>First assignment for <paramref name="requestType"/> along <paramref name="activePath"/>; null when none.</summary>
    public static EffectiveApprovalPolicy? Resolve(
        IReadOnlyList<Guid> activePath,
        IEnumerable<OrgUnitApprovalPolicyAssignment> assignments,
        string requestType)
    {
        var byUnit = assignments
            .Where(a => string.Equals(a.RequestType, requestType, StringComparison.Ordinal))
            .GroupBy(a => a.OrgUnitId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var unitId in activePath)
        {
            if (byUnit.TryGetValue(unitId, out var assignment))
            {
                return new EffectiveApprovalPolicy(assignment.ApprovalPolicyId, unitId);
            }
        }

        return null;
    }
}
