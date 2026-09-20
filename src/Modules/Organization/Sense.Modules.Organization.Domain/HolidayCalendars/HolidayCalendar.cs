using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.HolidayCalendars;

/// <summary>
/// A named set of holidays that a Company org unit can be assigned to (roadmap Faz 0 item #5, see
/// docs/analysis/kolayik-video/11.3.02-sirket-yapisi.md: "Şirket eklerken tatil takvimi seçilir ...
/// en alt yapıya kadar bütün yapılara etki eder"). Each tenant gets its own row for the seeded
/// "Türkiye Resmi Tatilleri" system calendar (<see cref="IsSystemDefined"/> = true, seeded once per
/// tenant by Sense.Api.SeedData.DataSeeder, mirroring how OrgUnitTypeCatalog seeds system hierarchy
/// levels per tenant) plus zero or more tenant-authored custom calendars (e.g. a second country's
/// holidays for a multi-country tenant, or a company-specific extra closure day).
/// Now wired as a real, same-schema FK from <see cref="Companies.Company.HolidayCalendarId"/>
/// (see OrganizationDbContext.CompanyConfiguration) - this was left as a reserved column with no FK by
/// roadmap item #4 specifically for this item to fill in.
/// </summary>
public sealed class HolidayCalendar : TenantAggregateRoot<Guid>, IAuditLogged
{
    private HolidayCalendar()
    {
    }

    private HolidayCalendar(Guid id, Guid tenantId, string name, string countryCode, bool isSystemDefined)
        : base(id, tenantId)
    {
        Name = name;
        CountryCode = countryCode;
        IsSystemDefined = isSystemDefined;
    }

    /// <summary>Display name, e.g. "Türkiye Resmi Tatilleri" or a tenant-chosen custom name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. "TR"); informational for now - Faz 0 does not branch behavior on it beyond the seeded TR calendar.</summary>
    public string CountryCode { get; private set; } = string.Empty;

    /// <summary>True for the seeded per-tenant "Türkiye Resmi Tatilleri" row; false for a tenant-authored custom calendar.</summary>
    public bool IsSystemDefined { get; private set; }

    public static Result<HolidayCalendar> Create(Guid tenantId, string name, string countryCode, bool isSystemDefined, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.HolidayCalendarNameRequired);
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return Error.Validation(OrganizationDomainErrors.HolidayCalendarCountryCodeRequired);
        }

        var normalizedCountryCode = countryCode.Trim().ToUpperInvariant();
        if (!IsoCountryCodes.IsValid(normalizedCountryCode))
        {
            return Error.Validation(OrganizationDomainErrors.HolidayCalendarCountryCodeInvalid);
        }

        var calendar = new HolidayCalendar(
            id ?? Guid.CreateVersion7(),
            tenantId,
            Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength),
            normalizedCountryCode,
            isSystemDefined);

        calendar.Raise(new HolidayCalendarCreated(calendar.Id, tenantId, calendar.Name, calendar.CountryCode, isSystemDefined));
        return calendar;
    }

    /// <summary>Renames a tenant-custom calendar; the seeded system calendar is read-only.</summary>
    public Result Rename(string name)
    {
        if (IsSystemDefined)
        {
            return Error.Rule(OrganizationDomainErrors.HolidayCalendarSystemImmutable);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.HolidayCalendarNameRequired);
        }

        Name = Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength);
        return Result.Success();
    }
}

public sealed record HolidayCalendarCreated(Guid HolidayCalendarId, Guid TenantId, string Name, string CountryCode, bool IsSystemDefined) : DomainEvent;
