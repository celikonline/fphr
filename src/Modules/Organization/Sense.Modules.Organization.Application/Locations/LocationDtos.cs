using Sense.Modules.Organization.Domain.Locations;

namespace Sense.Modules.Organization.Application.Locations;

/// <summary>
/// Location shape returned to the web Locations screen (web/src/services/locations.service.ts).
/// <paramref name="Latitude"/>/<paramref name="Longitude"/>/<paramref name="GeofenceRadiusMeters"/> (S-31): geofence
/// center + optional radius for GPS self-servis check-in validation. <paramref name="SelfCheckInCode"/>: the value
/// printed/encoded as this location's QR self-servis check-in code.
/// </summary>
public sealed record LocationDto(
    Guid Id,
    string Name,
    LocationType Type,
    string CountryCode,
    string? Address,
    bool IsActive,
    string? SgkIsyeriSicilNo,
    double? Latitude,
    double? Longitude,
    int? GeofenceRadiusMeters,
    string SelfCheckInCode);
