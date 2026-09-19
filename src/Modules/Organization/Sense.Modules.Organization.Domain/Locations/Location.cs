using System.Security.Cryptography;
using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.Locations;

/// <summary>
/// Hierarchy level of a physical (or virtual, for <see cref="LocationType.Remote"/>) place a tenant
/// operates from: office, retail store, warehouse, factory, or a fully remote "location" used purely
/// for grouping remote-only org units (roadmap Faz 0 item #6 / 11.3.02-sirket-yapisi.md: "Location
/// (plan: tür + ülke + geofence)").
/// </summary>
public enum LocationType
{
    Office = 1,
    Store = 2,
    Warehouse = 3,
    Factory = 4,
    Remote = 5,
}

/// <summary>
/// A physical office/store/warehouse/factory (or a Remote placeholder) that an <see cref="OrgUnits.OrgUnit"/>
/// (typically a Branch-level unit) can optionally point at via <see cref="OrgUnits.OrgUnit.LocationId"/>.
/// <b>Design decision</b>: the reference direction is OrgUnit -&gt; Location (one Location can be shared by
/// several org units, e.g. several departments at the same office), matching the column already reserved
/// on OrgUnit by roadmap item #4 and the doc's own note ("OrgUnit(Branch).LocationId? opsiyonel bağ
/// (adres/geofence Location'da kalır, Location Branch'siz de olabilir)"); Location itself carries no
/// back-reference to any OrgUnit. Same-module, same-schema as OrgUnit, so this gets a normal EF FK
/// (see OrganizationDbContext's OrgUnitConfiguration), unlike the placeholder Guid-only cross-module
/// references this codebase uses for Employee/HolidayCalendar.
/// </summary>
public sealed class Location : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Location()
    {
    }

    private Location(Guid id, Guid tenantId, string name, LocationType type, string countryCode, string? address, string? sgkIsyeriSicilNo)
        : base(id, tenantId)
    {
        Name = name;
        Type = type;
        CountryCode = countryCode;
        Address = address;
        SgkIsyeriSicilNo = sgkIsyeriSicilNo;
        IsActive = true;
        SelfCheckInCode = GenerateSelfCheckInCode();
    }

    public string Name { get; private set; } = string.Empty;

    public LocationType Type { get; private set; }

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "TR"), uppercase.</summary>
    public string CountryCode { get; private set; } = string.Empty;

    /// <summary>Free-text address; optional (e.g. a Remote-type location may not have one).</summary>
    public string? Address { get; private set; }

    /// <summary>
    /// SGK "işyeri sicil numarası" (APHB, senseik-010/F-03): lets a multi-workplace tenant define this per location
    /// instead of only at company level; free text (no fixed structure enforced here), optional.
    /// </summary>
    public string? SgkIsyeriSicilNo { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Geofence merkezi (S-31 self-servis GPS check-in): WGS84 derece. Latitude/Longitude ikisi de doludur veya
    /// ikisi de boştur (<see cref="SetGeofence"/>); <see cref="GeofenceRadiusMeters"/> yalnız ikisi de doluyken
    /// anlamlıdır - "opsiyonel geofence kontrolü" (kabul kriteri 2): yarıçap boşsa GPS check-in konumu kaydeder
    /// ama mesafe doğrulaması yapmaz.
    /// </summary>
    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public int? GeofenceRadiusMeters { get; private set; }

    /// <summary>
    /// QR self-servis check-in (S-31): bu lokasyonun girişine asılan QR kod bu değeri kodlar - taratan çalışanın
    /// cihazı bu kodu backend'e gönderir, Attendance modülü (ILocationLookup.FindBySelfCheckInCodeAsync) bunu bu
    /// lokasyona çözer. Oluşturma anında üretilir, tenant içinde benzersizdir (bkz. LocationConfiguration'daki
    /// unique index).
    /// </summary>
    public string SelfCheckInCode { get; private set; } = string.Empty;

    /// <summary>
    /// Issues a fresh QR self-check-in code, invalidating the old one (a leaked/photographed poster can be revoked without
    /// touching the rest of the location). The old code stops resolving immediately.
    /// </summary>
    public void RegenerateSelfCheckInCode() => SelfCheckInCode = GenerateSelfCheckInCode();

    public static Result<Location> Create(Guid tenantId, string name, LocationType type, string countryCode, string? address, string? sgkIsyeriSicilNo = null, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.LocationNameRequired);
        }

        if (!Enum.IsDefined(type))
        {
            return Error.Validation(OrganizationDomainErrors.LocationTypeInvalid);
        }

        var normalizedCountryCode = NormalizeCountryCode(countryCode);
        if (normalizedCountryCode is null)
        {
            return Error.Validation(OrganizationDomainErrors.LocationCountryCodeInvalid);
        }

        var location = new Location(
            id ?? Guid.CreateVersion7(),
            tenantId,
            Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength),
            type,
            normalizedCountryCode,
            NormalizeAddress(address),
            NormalizeSgkIsyeriSicilNo(sgkIsyeriSicilNo));

        location.Raise(new LocationCreated(location.Id, tenantId, location.Name, location.Type, location.CountryCode));
        return location;
    }

    public Result Update(string name, LocationType type, string countryCode, string? address, string? sgkIsyeriSicilNo = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.LocationNameRequired);
        }

        if (!Enum.IsDefined(type))
        {
            return Error.Validation(OrganizationDomainErrors.LocationTypeInvalid);
        }

        var normalizedCountryCode = NormalizeCountryCode(countryCode);
        if (normalizedCountryCode is null)
        {
            return Error.Validation(OrganizationDomainErrors.LocationCountryCodeInvalid);
        }

        Name = Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength);
        Type = type;
        CountryCode = normalizedCountryCode;
        Address = NormalizeAddress(address);
        SgkIsyeriSicilNo = NormalizeSgkIsyeriSicilNo(sgkIsyeriSicilNo);

        Raise(new LocationUpdated(Id, TenantId, Name, Type, CountryCode));
        return Result.Success();
    }

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(OrganizationDomainErrors.LocationAlreadyInactive);
        }

        IsActive = false;
        Raise(new LocationDeactivated(Id, TenantId));
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    /// <summary>
    /// Sets or clears the geofence (S-31 kabul kriteri 2). Pass all three null to clear it. Radius requires a
    /// center point; a center point without a radius is allowed (konum kaydedilir, mesafe doğrulaması yapılmaz).
    /// </summary>
    public Result SetGeofence(double? latitude, double? longitude, int? radiusMeters)
    {
        if (latitude is null != longitude is null)
        {
            return Error.Validation(OrganizationDomainErrors.LocationGeofenceInvalid);
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return Error.Validation(OrganizationDomainErrors.LocationGeofenceInvalid);
        }

        if (radiusMeters is <= 0)
        {
            return Error.Validation(OrganizationDomainErrors.LocationGeofenceInvalid);
        }

        if (radiusMeters is not null && latitude is null)
        {
            return Error.Validation(OrganizationDomainErrors.LocationGeofenceInvalid);
        }

        Latitude = latitude;
        Longitude = longitude;
        GeofenceRadiusMeters = latitude is null ? null : radiusMeters;
        return Result.Success();
    }

    /// <summary>Alphabet excludes visually ambiguous characters (0/O, 1/I/L) - printed on a QR poster, may be read by eye as a fallback.</summary>
    private const string SelfCheckInCodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>12 chars over a 31-symbol alphabet is ~59 bits of CSPRNG entropy - not guessable/enumerable through the (authenticated, tenant-scoped) QR endpoint.</summary>
    public const int SelfCheckInCodeLength = 12;

    private static string GenerateSelfCheckInCode() =>
        string.Create(SelfCheckInCodeLength, 0, static (chars, _) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = SelfCheckInCodeAlphabet[RandomNumberGenerator.GetInt32(SelfCheckInCodeAlphabet.Length)];
            }
        });

    private static string? NormalizeAddress(string? address) =>
        string.IsNullOrWhiteSpace(address) ? null : Guard.MaxLength(address.Trim(), OrganizationLimits.AddressMaxLength);

    private static string? NormalizeSgkIsyeriSicilNo(string? sgkIsyeriSicilNo) =>
        string.IsNullOrWhiteSpace(sgkIsyeriSicilNo) ? null : Guard.MaxLength(sgkIsyeriSicilNo.Trim(), OrganizationLimits.SgkIsyeriSicilNoMaxLength);

    private static string? NormalizeCountryCode(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var trimmed = countryCode.Trim().ToUpperInvariant();
        return trimmed.Length == OrganizationLimits.CountryCodeLength && trimmed.All(c => c is >= 'A' and <= 'Z')
            ? trimmed
            : null;
    }
}

public sealed record LocationCreated(Guid LocationId, Guid TenantId, string Name, LocationType Type, string CountryCode) : DomainEvent;

public sealed record LocationUpdated(Guid LocationId, Guid TenantId, string Name, LocationType Type, string CountryCode) : DomainEvent;

public sealed record LocationDeactivated(Guid LocationId, Guid TenantId) : DomainEvent;
