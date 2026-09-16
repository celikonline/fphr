using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.Companies;

/// <summary>
/// Company-level settings attached to the tenant's root org unit (the OrgUnit whose TypeId resolves
/// to OrgUnitTypeCodes.Company). <b>Design decision</b> (roadmap item #4, see also
/// docs/analysis/kolayik-video/11.3.02-sirket-yapisi.md, which already specifies
/// "Company{OrgUnitId, ..., HolidayCalendarId, JointEmploymentGroupCode?}"): Company is its own small
/// aggregate, 1:1 with an OrgUnit via <see cref="OrgUnitId"/>, rather than
///   (a) folding these fields directly onto OrgUnit - which would leak company-only concerns
///       (holiday calendar, joint-employment grouping) onto every hierarchy level, since OrgUnit is
///       shared by Branch/Department/Title/etc. rows too, or
///   (b) folding them onto Identity.Tenant - Tenant is a distinct bounded concept (the billing/auth
///       account) and is explicitly out of scope to modify for this item; Tenant.SettingsJson stays
///       untouched. A tenant's legal "company" (in the org-chart sense the video describes: "Şirket
///       eklerken tatil takvimi seçilir") is not the same thing as its SaaS account.
/// A tenant may have more than one Company row in the future (multi-legal-entity groups sharing one
/// tenant, see <see cref="JointEmploymentGroupCode"/>); Faz 0 seed creates exactly one per tenant,
/// attached to the single root OrgUnit(Company) created alongside it.
/// </summary>
public sealed class Company : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Company()
    {
    }

    private Company(Guid id, Guid tenantId, Guid orgUnitId)
        : base(id, tenantId)
    {
        OrgUnitId = orgUnitId;
    }

    /// <summary>The OrgUnit (Type=Company) this settings row belongs to. Unique per tenant (1:1).</summary>
    public Guid OrgUnitId { get; private set; }

    /// <summary>
    /// Same-module reference to <see cref="HolidayCalendars.HolidayCalendar"/> (roadmap Faz 0 item #5;
    /// previously a reserved column with no FK - see OrganizationDbContext.CompanyConfiguration for the
    /// real FK now wired here). Null means "no calendar assigned yet"; resolution to an effective
    /// calendar (with a fallback to the tenant's seeded system TR calendar) is the concern of
    /// Sense.Modules.Organization.Contracts.IHolidayCalendarLookup, not of this property.
    /// </summary>
    public Guid? HolidayCalendarId { get; private set; }

    /// <summary>
    /// Groups tenants/companies for cross-entity seniority continuity and combined 30-employee job-
    /// security thresholds (roadmap item #36, Faz 1+ legislation work). Field only in Faz 0 - no
    /// resolution logic yet.
    /// </summary>
    public string? JointEmploymentGroupCode { get; private set; }

    /// <summary>Legal/registry fields (Faz 2, TB-10 "Şirket profili"). All optional - a Company row exists before anyone fills these in.</summary>
    public string? RegistrationNumber { get; private set; }

    /// <summary>Tax number (vergi no). Deliberately not encrypted like TCKN/IBAN - this is public trade-registry data, not a KVKK special category.</summary>
    public string? TaxNumber { get; private set; }

    public string? Address { get; private set; }

    public string? City { get; private set; }

    public string? Country { get; private set; }

    public TimeOnly? OperatingHoursStart { get; private set; }

    public TimeOnly? OperatingHoursEnd { get; private set; }

    /// <summary>Comma-separated lowercase weekday codes (<see cref="CompanyWorkingDays.Codes"/>), e.g. "mon,tue,wed,thu,fri". Null/empty means unset.</summary>
    public string? WorkingDaysCsv { get; private set; }

    /// <summary>Document module reference for the uploaded logo (Faz 2 Documents integration); resolving a durable display URL for it is future work.</summary>
    public Guid? LogoDocumentId { get; private set; }

    public string? LogoUrl { get; private set; }

    public IReadOnlyList<string> WorkingDays =>
        string.IsNullOrWhiteSpace(WorkingDaysCsv)
            ? []
            : WorkingDaysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Result<Company> Create(Guid tenantId, Guid orgUnitId, Guid? holidayCalendarId = null, Guid? id = null)
    {
        if (orgUnitId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.CompanyOrgUnitRequired);
        }

        var company = new Company(id ?? Guid.CreateVersion7(), tenantId, orgUnitId)
        {
            HolidayCalendarId = holidayCalendarId,
        };

        company.Raise(new CompanyCreated(company.Id, tenantId, orgUnitId));
        return company;
    }

    public void SetHolidayCalendar(Guid? holidayCalendarId)
    {
        HolidayCalendarId = holidayCalendarId;
        Raise(new CompanyHolidayCalendarChanged(Id, TenantId, holidayCalendarId));
    }

    public void SetJointEmploymentGroupCode(string? code) =>
        JointEmploymentGroupCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    /// <summary>
    /// Sets the legal/registry profile fields in one call (Faz 2, TB-10 "Şirket profili": tenant profile
    /// screen edits all of these together). Working days are validated/normalized by the caller
    /// (<see cref="CompanyWorkingDays"/>) - this aggregate only stores the already-normalized CSV.
    /// </summary>
    public void UpdateProfile(
        string? registrationNumber,
        string? taxNumber,
        string? address,
        string? city,
        string? country,
        TimeOnly? operatingHoursStart,
        TimeOnly? operatingHoursEnd,
        IReadOnlyList<string>? workingDays)
    {
        RegistrationNumber = Trimmed(registrationNumber);
        TaxNumber = Trimmed(taxNumber);
        Address = Trimmed(address);
        City = Trimmed(city);
        Country = Trimmed(country);
        OperatingHoursStart = operatingHoursStart;
        OperatingHoursEnd = operatingHoursEnd;
        WorkingDaysCsv = workingDays is { Count: > 0 } ? string.Join(',', workingDays) : null;

        Raise(new CompanyProfileUpdated(Id, TenantId));
    }

    /// <summary>Records the uploaded logo's Document id and current display URL (see <see cref="LogoUrl"/> remarks).</summary>
    public void SetLogo(Guid? documentId, string? url)
    {
        LogoDocumentId = documentId;
        LogoUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Canonical weekday codes for <see cref="Company.WorkingDaysCsv"/>, matching the web `WeekDay` union type.</summary>
public static class CompanyWorkingDays
{
    public static readonly IReadOnlyList<string> Codes = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
}

public sealed record CompanyCreated(Guid CompanyId, Guid TenantId, Guid OrgUnitId) : DomainEvent;

public sealed record CompanyHolidayCalendarChanged(Guid CompanyId, Guid TenantId, Guid? HolidayCalendarId) : DomainEvent;

public sealed record CompanyProfileUpdated(Guid CompanyId, Guid TenantId) : DomainEvent;
