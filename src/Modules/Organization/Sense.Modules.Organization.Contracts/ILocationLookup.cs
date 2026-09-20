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

    /// <summary>
    /// Resolves location names for display (S-19, Faz 9 kariyer sayfası). Includes inactive locations
    /// (a closed posting may still reference a now-inactive location and should still show its name);
    /// ids with no matching location for this tenant are simply absent from the result - callers must
    /// not assume every requested id comes back.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves each location's SGK "işyeri sicil numarası" (senseik-010/F-03), for Payroll's APHB/e-Bildirge export
    /// preparation (the export itself is out of scope; this only surfaces the stored value). A location with no
    /// value set, or not found for this tenant, is simply absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetSgkIsyeriSicilNoAsync(Guid tenantId, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);

    /// <summary>Geofence + name for GPS self-servis check-in validation (S-31, Attendance module). Null if inactive or not found.</summary>
    Task<LocationGeofence?> GetGeofenceAsync(Guid tenantId, Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>Resolves the location a QR self-servis check-in code (Location.SelfCheckInCode) points at (S-31). Null if no active location has this code.</summary>
    Task<LocationGeofence?> FindBySelfCheckInCodeAsync(Guid tenantId, string code, CancellationToken cancellationToken = default);
}

/// <summary>Cross-module read of a Location's geofence (S-31); <paramref name="Latitude"/>/<paramref name="Longitude"/>/<paramref name="RadiusMeters"/> null when no geofence is configured for it.</summary>
public sealed record LocationGeofence(Guid LocationId, string Name, double? Latitude, double? Longitude, int? RadiusMeters);
