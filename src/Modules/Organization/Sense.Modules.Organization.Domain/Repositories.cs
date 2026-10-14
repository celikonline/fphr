using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain;

/// <summary>OrgUnitType aggregate repository.</summary>
public interface IOrgUnitTypeRepository
{
    void Add(OrgUnitType type);

    Task<Result<OrgUnitType>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrgUnitType>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    Task<OrgUnitType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>OrgUnit aggregate repository.</summary>
public interface IOrgUnitRepository
{
    void Add(OrgUnit unit);

    Task<Result<OrgUnit>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Flat list for the current tenant; caller builds a tree from ParentId if needed (see Application.OrgUnits.OrgUnitTreeBuilder).</summary>
    Task<IReadOnlyList<OrgUnit>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrgUnit>> ListByTypeAsync(Guid typeId, bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<bool> HasChildrenAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    /// <summary>True if any active org unit currently points at this location (used to block Location deactivation while referenced).</summary>
    Task<bool> HasActiveLocationReferenceAsync(Guid locationId, CancellationToken cancellationToken = default);
}

/// <summary>Location aggregate repository.</summary>
public interface ILocationRepository
{
    void Add(Location location);

    Task<Result<Location>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Location>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid locationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// CostCenter aggregate repository (roadmap Faz 2 item #2, S-4). Placed in Domain rather than Application
/// to match every other repository in this file (TALIMAT tur 1 said "(Application)" in passing; kept the
/// established convention instead - flagged in the turn's report for PO to correct if the parenthetical
/// was deliberate).
/// </summary>
public interface ICostCenterRepository
{
    void Add(CostCenter costCenter);

    Task<Result<CostCenter>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CostCenter>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exact (case-sensitive) match against <paramref name="code"/>. Application-layer callers normalize
    /// to trimmed uppercase before calling (e.g. the create handler) because <see cref="CostCenter.Create"/>
    /// stores <c>Code</c> uppercased - do not add case-insensitive comparison here, it would just hide a
    /// caller bug instead of matching the stored value.
    /// </summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>Optional employee-level override of the cost center inherited from the org-unit hierarchy.</summary>
public interface IEmployeeCostCenterOverrideRepository
{
    Task<EmployeeCostCenterOverride?> FindByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeCostCenterOverride>> ListByEmployeeIdsAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default);

    void Add(EmployeeCostCenterOverride assignment);

    void Remove(EmployeeCostCenterOverride assignment);
}

/// <summary>Company aggregate repository.</summary>
public interface ICompanyRepository
{
    void Add(Company company);

    Task<Result<Company>> GetByOrgUnitIdAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    Task<Result<Company>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>OrgUnitApprovalPolicyAssignment repository (unique per tenant, org unit and request type).</summary>
public interface IOrgUnitApprovalPolicyAssignmentRepository
{
    void Add(OrgUnitApprovalPolicyAssignment assignment);

    void Remove(OrgUnitApprovalPolicyAssignment assignment);

    Task<IReadOnlyList<OrgUnitApprovalPolicyAssignment>> ListByOrgUnitAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    /// <summary>Assignments of all request types on any of the given units (tracked, so the caller can change them).</summary>
    Task<IReadOnlyList<OrgUnitApprovalPolicyAssignment>> ListByOrgUnitsAsync(IReadOnlyCollection<Guid> orgUnitIds, CancellationToken cancellationToken = default);
}

/// <summary>HolidayCalendar aggregate repository.</summary>
public interface IHolidayCalendarRepository
{
    void Add(HolidayCalendar calendar);

    /// <summary>Removes the calendar; its entries go with it (DB cascade).</summary>
    void Remove(HolidayCalendar calendar);

    /// <summary>True when any Company of the current tenant has this calendar assigned.</summary>
    Task<bool> IsAssignedToCompanyAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default);

    Task<Result<HolidayCalendar>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HolidayCalendar>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>All calendars for the current tenant (system-seeded TR calendar + any tenant-authored custom ones).</summary>
    Task<IReadOnlyList<HolidayCalendar>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>HolidayCalendarEntry aggregate repository.</summary>
public interface IHolidayCalendarEntryRepository
{
    void Add(HolidayCalendarEntry entry);

    void Remove(HolidayCalendarEntry entry);

    /// <summary>The entry (tracked) if it belongs to the given calendar, else null.</summary>
    Task<HolidayCalendarEntry?> FindAsync(Guid holidayCalendarId, Guid entryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HolidayCalendarEntry>> ListByCalendarAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default);
}
