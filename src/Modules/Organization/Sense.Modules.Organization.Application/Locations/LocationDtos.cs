using Sense.Modules.Organization.Domain.Locations;

namespace Sense.Modules.Organization.Application.Locations;

/// <summary>Location shape returned to the web Locations screen (web/src/services/locations.service.ts).</summary>
public sealed record LocationDto(
    Guid Id,
    string Name,
    LocationType Type,
    string CountryCode,
    string? Address,
    bool IsActive,
    string? SgkIsyeriSicilNo = null);
