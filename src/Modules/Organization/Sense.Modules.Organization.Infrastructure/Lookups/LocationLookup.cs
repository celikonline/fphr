using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Implements Organization.Contracts.ILocationLookup so other modules can validate a LocationId reference.</summary>
public sealed class LocationLookup(OrganizationDbContext context) : ILocationLookup
{
    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid locationId, CancellationToken cancellationToken = default) =>
        await context.Locations.AnyAsync(l => l.TenantId == tenantId && l.Id == locationId && l.IsActive, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        if (locationIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = locationIds.ToList();
        return await context.Locations
            .Where(l => l.TenantId == tenantId && ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSgkIsyeriSicilNoAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        if (locationIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = locationIds.ToList();
        return await context.Locations
            .Where(l => l.TenantId == tenantId && ids.Contains(l.Id) && l.SgkIsyeriSicilNo != null)
            .ToDictionaryAsync(l => l.Id, l => l.SgkIsyeriSicilNo!, cancellationToken);
    }

    public async Task<LocationGeofence?> GetGeofenceAsync(Guid tenantId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var location = await context.Locations
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.Id == locationId && l.IsActive, cancellationToken);
        return location is null ? null : new LocationGeofence(location.Id, location.Name, location.Latitude, location.Longitude, location.GeofenceRadiusMeters);
    }

    public async Task<LocationGeofence?> FindBySelfCheckInCodeAsync(Guid tenantId, string code, CancellationToken cancellationToken = default)
    {
        var location = await context.Locations
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive && l.SelfCheckInCode == code, cancellationToken);
        return location is null ? null : new LocationGeofence(location.Id, location.Name, location.Latitude, location.Longitude, location.GeofenceRadiusMeters);
    }
}
