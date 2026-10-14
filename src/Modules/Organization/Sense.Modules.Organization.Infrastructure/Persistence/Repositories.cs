using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Domain;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Infrastructure.Persistence;

/// <summary>EF Core-backed OrgUnitType repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class OrgUnitTypeRepository(OrganizationDbContext context) : IOrgUnitTypeRepository
{
    public void Add(OrgUnitType type) => context.OrgUnitTypes.Add(type);

    public async Task<Result<OrgUnitType>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var type = await context.OrgUnitTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return type is null ? Error.NotFound(OrganizationDomainErrors.OrgUnitTypeNotFound) : type;
    }

    public async Task<IReadOnlyList<OrgUnitType>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = context.OrgUnitTypes.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(t => t.IsActive);
        }

        return await query.OrderBy(t => t.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        await context.OrgUnitTypes.AnyAsync(t => t.Code == code, cancellationToken);

    public async Task<OrgUnitType?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        await context.OrgUnitTypes.FirstOrDefaultAsync(t => t.Code == code, cancellationToken);

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        await context.OrgUnitTypes.CountAsync(cancellationToken);
}

/// <summary>EF Core-backed OrgUnit repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class OrgUnitRepository(OrganizationDbContext context) : IOrgUnitRepository
{
    public void Add(OrgUnit unit) => context.OrgUnits.Add(unit);

    public async Task<Result<OrgUnit>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var unit = await context.OrgUnits.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return unit is null ? Error.NotFound(OrganizationDomainErrors.OrgUnitNotFound) : unit;
    }

    public async Task<IReadOnlyList<OrgUnit>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = context.OrgUnits.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(u => u.IsActive);
        }

        return await query.OrderBy(u => u.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrgUnit>> ListByTypeAsync(Guid typeId, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = context.OrgUnits.Where(u => u.TypeId == typeId);
        if (activeOnly)
        {
            query = query.Where(u => u.IsActive);
        }

        return await query.OrderBy(u => u.SortOrder).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasChildrenAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.ParentId == orgUnitId && u.IsActive, cancellationToken);

    public async Task<bool> ExistsActiveAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.Id == orgUnitId && u.IsActive, cancellationToken);

    public async Task<bool> HasActiveLocationReferenceAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.LocationId == locationId && u.IsActive, cancellationToken);
}

/// <summary>EF Core-backed Location repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class LocationRepository(OrganizationDbContext context) : ILocationRepository
{
    public void Add(Location location) => context.Locations.Add(location);

    public async Task<Result<Location>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var location = await context.Locations.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        return location is null ? Error.NotFound(OrganizationDomainErrors.LocationNotFound) : location;
    }

    public async Task<IReadOnlyList<Location>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = context.Locations.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(l => l.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsActiveAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        await context.Locations.AnyAsync(l => l.Id == locationId && l.IsActive, cancellationToken);
}

/// <summary>EF Core-backed Company repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class CompanyRepository(OrganizationDbContext context) : ICompanyRepository
{
    public void Add(Company company) => context.Companies.Add(company);

    public async Task<Result<Company>> GetByOrgUnitIdAsync(Guid orgUnitId, CancellationToken cancellationToken = default)
    {
        var company = await context.Companies.FirstOrDefaultAsync(c => c.OrgUnitId == orgUnitId, cancellationToken);
        return company is null ? Error.NotFound(OrganizationDomainErrors.CompanyNotFound) : company;
    }

    public async Task<Result<Company>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var company = await context.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return company is null ? Error.NotFound(OrganizationDomainErrors.CompanyNotFound) : company;
    }

    public async Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Companies.OrderBy(c => c.Id).ToListAsync(cancellationToken);
}

/// <summary>EF Core-backed CostCenter repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class CostCenterRepository(OrganizationDbContext context) : ICostCenterRepository
{
    public void Add(CostCenter costCenter) => context.CostCenters.Add(costCenter);

    public async Task<Result<CostCenter>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var costCenter = await context.CostCenters.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return costCenter is null ? Error.NotFound(OrganizationDomainErrors.CostCenterNotFound) : costCenter;
    }

    public async Task<IReadOnlyList<CostCenter>> ListAllAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = context.CostCenters.AsQueryable();
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.OrderBy(c => c.Code).ToListAsync(cancellationToken);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default) =>
        await context.CostCenters.AnyAsync(c => c.Code == code, cancellationToken);
}

public sealed class EmployeeCostCenterOverrideRepository(OrganizationDbContext context) : IEmployeeCostCenterOverrideRepository
{
    public Task<EmployeeCostCenterOverride?> FindByEmployeeIdAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        context.EmployeeCostCenterOverrides.FirstOrDefaultAsync(x => x.EmployeeId == employeeId, cancellationToken);

    public async Task<IReadOnlyList<EmployeeCostCenterOverride>> ListByEmployeeIdsAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default) =>
        employeeIds.Count == 0
            ? []
            : await context.EmployeeCostCenterOverrides.Where(x => employeeIds.Contains(x.EmployeeId)).ToListAsync(cancellationToken);

    public void Add(EmployeeCostCenterOverride assignment) => context.EmployeeCostCenterOverrides.Add(assignment);

    public void Remove(EmployeeCostCenterOverride assignment) => context.EmployeeCostCenterOverrides.Remove(assignment);
}

/// <summary>EF Core-backed OrgUnitApprovalPolicyAssignment repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class OrgUnitApprovalPolicyAssignmentRepository(OrganizationDbContext context) : IOrgUnitApprovalPolicyAssignmentRepository
{
    public void Add(OrgUnitApprovalPolicyAssignment assignment) => context.ApprovalPolicyAssignments.Add(assignment);

    public void Remove(OrgUnitApprovalPolicyAssignment assignment) => context.ApprovalPolicyAssignments.Remove(assignment);

    public async Task<IReadOnlyList<OrgUnitApprovalPolicyAssignment>> ListByOrgUnitsAsync(IReadOnlyCollection<Guid> orgUnitIds, CancellationToken cancellationToken = default)
    {
        if (orgUnitIds.Count == 0)
        {
            return [];
        }

        var ids = orgUnitIds.ToList();
        return await context.ApprovalPolicyAssignments.Where(a => ids.Contains(a.OrgUnitId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrgUnitApprovalPolicyAssignment>> ListByOrgUnitAsync(Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.ApprovalPolicyAssignments.Where(a => a.OrgUnitId == orgUnitId).ToListAsync(cancellationToken);
}

/// <summary>EF Core-backed HolidayCalendar repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class HolidayCalendarRepository(OrganizationDbContext context) : IHolidayCalendarRepository
{
    public void Add(HolidayCalendar calendar) => context.HolidayCalendars.Add(calendar);

    public void Remove(HolidayCalendar calendar) => context.HolidayCalendars.Remove(calendar);

    public async Task<bool> IsAssignedToCompanyAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default) =>
        await context.Companies.AnyAsync(c => c.HolidayCalendarId == holidayCalendarId, cancellationToken);

    public async Task<Result<HolidayCalendar>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var calendar = await context.HolidayCalendars.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return calendar is null ? Error.NotFound(OrganizationDomainErrors.HolidayCalendarNotFound) : calendar;
    }

    public async Task<IReadOnlyList<HolidayCalendar>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var list = ids.ToList();
        return await context.HolidayCalendars.Where(c => list.Contains(c.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HolidayCalendar>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.HolidayCalendars.OrderBy(c => c.Name).ToListAsync(cancellationToken);
}

/// <summary>EF Core-backed HolidayCalendarEntry repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class HolidayCalendarEntryRepository(OrganizationDbContext context) : IHolidayCalendarEntryRepository
{
    public void Add(HolidayCalendarEntry entry) => context.HolidayCalendarEntries.Add(entry);

    public void Remove(HolidayCalendarEntry entry) => context.HolidayCalendarEntries.Remove(entry);

    public async Task<HolidayCalendarEntry?> FindAsync(Guid holidayCalendarId, Guid entryId, CancellationToken cancellationToken = default) =>
        await context.HolidayCalendarEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.HolidayCalendarId == holidayCalendarId, cancellationToken);

    public async Task<IReadOnlyList<HolidayCalendarEntry>> ListByCalendarAsync(Guid holidayCalendarId, CancellationToken cancellationToken = default) =>
        await context.HolidayCalendarEntries
            .Where(e => e.HolidayCalendarId == holidayCalendarId)
            .OrderBy(e => e.Date)
            .ToListAsync(cancellationToken);
}
