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

    /// <summary>Cross-module reference to a future HolidayCalendar entity (different roadmap item); column reserved, no FK yet.</summary>
    public Guid? HolidayCalendarId { get; private set; }

    /// <summary>
    /// Groups tenants/companies for cross-entity seniority continuity and combined 30-employee job-
    /// security thresholds (roadmap item #36, Faz 1+ legislation work). Field only in Faz 0 - no
    /// resolution logic yet.
    /// </summary>
    public string? JointEmploymentGroupCode { get; private set; }

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
}

public sealed record CompanyCreated(Guid CompanyId, Guid TenantId, Guid OrgUnitId) : DomainEvent;

public sealed record CompanyHolidayCalendarChanged(Guid CompanyId, Guid TenantId, Guid? HolidayCalendarId) : DomainEvent;
