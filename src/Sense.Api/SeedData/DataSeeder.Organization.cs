using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Domain.Users;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Leave.Application;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Balances;
using Sense.Modules.Leave.Domain.Ledger;
using Sense.Modules.Leave.Domain.Profiles;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Modules.Leave.Infrastructure.Persistence;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Modules.Organization.Infrastructure.Persistence.Seed;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Api.SeedData;

public partial class DataSeeder
{
    private readonly record struct OrgUnitTypeIds(Guid Company, Guid Department, Guid Title);

    /// <summary>Seeds the system OrgUnitType catalog for a tenant; returns the Company, Department and Title level ids for convenience.</summary>
    private OrgUnitTypeIds SeedOrgUnitTypes(Guid tenantId)
    {
        var companyTypeId = Guid.Empty;
        var departmentTypeId = Guid.Empty;
        var titleTypeId = Guid.Empty;

        foreach (var result in OrgUnitTypeCatalog.CreateAll(tenantId))
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to seed org unit type: {result.Error}");
            }

            organizationCtx.OrgUnitTypes.Add(result.Value);

            switch (result.Value.Code)
            {
                case OrgUnitTypeCodes.Company:
                    companyTypeId = result.Value.Id;
                    break;
                case OrgUnitTypeCodes.Department:
                    departmentTypeId = result.Value.Id;
                    break;
                case OrgUnitTypeCodes.Title:
                    titleTypeId = result.Value.Id;
                    break;
            }
        }

        return new OrgUnitTypeIds(companyTypeId, departmentTypeId, titleTypeId);
    }

    /// <summary>Title-level org units created on first use, one per (parent unit, name).</summary>
    private sealed class DemoTitleCatalog(Guid tenantId, Guid titleTypeId, OrganizationDbContext organizationCtx)
    {
        private readonly Dictionary<(Guid ParentId, string Name), OrgUnit> _titles = [];

        public int Count => _titles.Count;

        public OrgUnit GetOrCreate(OrgUnit parent, string name)
        {
            if (!_titles.TryGetValue((parent.Id, name), out var title))
            {
                var sortOrder = _titles.Keys.Count(k => k.ParentId == parent.Id);
                var result = OrgUnit.Create(tenantId, titleTypeId, parent.Id, name, sortOrder);
                if (result.IsFailure)
                {
                    throw new InvalidOperationException($"Failed to seed title '{name}': {result.Error}");
                }

                title = result.Value;
                organizationCtx.OrgUnits.Add(title);
                _titles[(parent.Id, name)] = title;
            }

            return title;
        }
    }

    private static OrgUnit CreateOrgUnitOrThrow(Guid tenantId, Guid typeId, Guid? parentId, string name)
    {
        var result = OrgUnit.Create(tenantId, typeId, parentId, name);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed org unit '{name}': {result.Error}");
        }

        return result.Value;
    }

    private static Company CreateCompanyOrThrow(Guid tenantId, Guid orgUnitId, Guid? holidayCalendarId = null)
    {
        var result = Company.Create(tenantId, orgUnitId, holidayCalendarId);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed company for org unit '{orgUnitId}': {result.Error}");
        }

        return result.Value;
    }

    /// <summary>
    /// Seeds one tenant's own copy of the system "Türkiye Resmi Tatilleri" calendar (roadmap Faz 0 item #5). The data (embedded
    /// src/Sense.Api/SeedData/holidays/tr-official-holidays.json) and its interpretation live in
    /// <see cref="TrHolidayCalendarSeed"/>, shared with the runtime tenant bootstrap. Adds the calendar and all its entries to
    /// organizationCtx; caller still owns SaveChangesAsync.
    /// </summary>
    private HolidayCalendar SeedTrHolidayCalendar(Guid tenantId) => TrHolidayCalendarSeed.AddTo(organizationCtx, tenantId);

    private static Location CreateLocationOrThrow(Guid tenantId, string name, LocationType type, string countryCode, string? address, string? sgkIsyeriSicilNo = null)
    {
        var result = Location.Create(tenantId, name, type, countryCode, address, sgkIsyeriSicilNo);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed location '{name}': {result.Error}");
        }

        return result.Value;
    }

    private static Employee CreateEmployeeOrThrow(Guid tenantId, EmployeeInfo info, Guid? userId, Guid? orgUnitId)
    {
        var result = Employee.Create(tenantId, info, userId, orgUnitId);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to seed employee '{info.FirstName} {info.LastName}': {result.Error}");
        }

        return result.Value;
    }

    /// <summary>
    /// Mirrors CreateEmployeeHandler's post-create step: the seeder builds Employee entities directly (not via the
    /// application command), so without this, EmployeeHired never reaches the outbox and modules that provision on
    /// hire (e.g. Leave's EmployeeLeaveProfile) never see these seeded employees.
    /// </summary>
    private void EnqueueHiredIfActive(Guid tenantId, Employee employee)
    {
        if (employee.Status == EmployeeStatus.Active && employee.HireDate is { } hireDate)
        {
            outbox.Enqueue(new EmployeeHired(tenantId, employee.Id, hireDate, employee.LeaveAccrualStartDate ?? hireDate));
        }
    }
}
