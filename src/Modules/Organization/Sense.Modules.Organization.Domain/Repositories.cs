using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Domain.Companies;
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

/// <summary>Company aggregate repository.</summary>
public interface ICompanyRepository
{
    void Add(Company company);

    Task<Result<Company>> GetByOrgUnitIdAsync(Guid orgUnitId, CancellationToken cancellationToken = default);

    Task<Result<Company>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>OrgUnitApprovalPolicyAssignment repository (stub consumer: future Workflow module).</summary>
public interface IOrgUnitApprovalPolicyAssignmentRepository
{
    void Add(OrgUnitApprovalPolicyAssignment assignment);

    Task<IReadOnlyList<OrgUnitApprovalPolicyAssignment>> ListByOrgUnitAsync(Guid orgUnitId, CancellationToken cancellationToken = default);
}

/// <summary>HolidayCalendar aggregate repository.</summary>
public interface IHolidayCalendarRepository
{
    void Add(HolidayCalendar calendar);

    Task<Result<HolidayCalendar>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All calendars for the current tenant (system-seeded TR calendar + any tenant-authored custom ones).</summary>
    Task<IReadOnlyList<HolidayCalendar>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>HolidayCalendarEntry aggregate repository.</summary>
public interface IHolidayCalendarEntryRepository
{
    void Add(HolidayCalendarEntry entry);

    Task<IReadOnlyList<HolidayCalendarEntry>> ListByCalendarAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default);
}
