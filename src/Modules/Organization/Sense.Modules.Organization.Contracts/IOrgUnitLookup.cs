namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets other modules (e.g. Employee, when creating/updating an
/// employee's OrgUnitId) validate an org unit reference without depending on Organization's Domain
/// or Infrastructure. Mirrors the pattern this codebase already uses for cross-module reads (see
/// Sense.Shared.Contracts.Security.IEmployeeScopeLookup, implemented by Employee, consumed by
/// Identity). Implemented by Sense.Modules.Organization.Infrastructure and registered by
/// OrganizationModule.AddModule.
/// </summary>
public interface IOrgUnitLookup
{
    /// <summary>True if an active org unit with this id exists for the given tenant.</summary>
    Task<bool> ExistsActiveAsync(Guid tenantId, Guid orgUnitId, CancellationToken cancellationToken = default);

    /// <summary>The given active units plus all of their active descendants (inactive/unknown ids are dropped; an inactive unit's subtree is not walked).</summary>
    Task<IReadOnlyList<Guid>> GetSelfAndDescendantIdsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken ct = default);

    /// <summary>Active hierarchy levels ordered by SortOrder (Company first).</summary>
    Task<IReadOnlyList<OrgUnitTypeInfo>> ListActiveTypesAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>All active org units of the tenant (flat; ParentId gives the tree).</summary>
    Task<IReadOnlyList<OrgUnitInfo>> ListActiveUnitsAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// The unit's workplace location: its own LocationId, else the nearest ancestor's. Null when no unit on the path
    /// to the root has a location (or the unit does not exist). Used by OHS to find an employee's hazard class.
    /// </summary>
    Task<Guid?> GetEffectiveLocationIdAsync(Guid tenantId, Guid orgUnitId, CancellationToken ct = default);

    /// <summary>Every unit that resolves to a location (same rule as <see cref="GetEffectiveLocationIdAsync"/>), unitId -> locationId.</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetEffectiveLocationMapAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Active hierarchy level (OrgUnitType) as seen by other modules.</summary>
public sealed record OrgUnitTypeInfo(Guid Id, string Code, string Name, int SortOrder);

/// <summary>Active org unit as seen by other modules.</summary>
public sealed record OrgUnitInfo(Guid Id, Guid TypeId, Guid? ParentId, string Name);
