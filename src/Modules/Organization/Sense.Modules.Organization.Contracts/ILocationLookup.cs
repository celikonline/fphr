namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Read-only cross-module lookup for Location (roadmap item #6), mirroring <see cref="IOrgUnitLookup"/>.
/// Lets other modules (e.g. a future Attendance PDKS checkpoint, or Employee's own OrgUnit validation
/// path) validate a LocationId reference without depending on Organization's Domain or Infrastructure.
/// Implemented by Sense.Modules.Organization.Infrastructure and registered by OrganizationModule.AddModule.
/// </summary>
public interface ILocationLookup
{
    /// <summary>True if an active location with this id exists for the given tenant.</summary>
    Task<bool> ExistsActiveAsync(Guid tenantId, Guid locationId, CancellationToken cancellationToken = default);
}
