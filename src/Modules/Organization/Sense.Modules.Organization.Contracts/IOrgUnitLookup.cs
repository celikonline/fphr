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
}
