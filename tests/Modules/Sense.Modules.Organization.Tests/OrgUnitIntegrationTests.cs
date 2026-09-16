using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Domain.Companies;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;
// Alias needed: any code nested under Sense.Modules.* sees "Sense.Modules.Employee" as a namespace
// member of its ancestor "Sense.Modules" as soon as an Employee-module assembly is referenced, and
// that direct namespace match wins over the "Employee" class brought in by a using-directive
// (CS0118) - see Sense.Modules.Employee.Domain.Repositories.cs for the same issue in the module itself.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Integration test (real PostgreSQL via Testcontainers, see TestFixture): creating an org unit tree
/// and assigning a manager employee, exercising the actual EF Core mappings/migrations for both
/// modules rather than mocking them out. Goes directly through each module's DbContext/domain layer
/// rather than Dispatcher.Send(...): the shared TestFixture's tenant/user helpers
/// (CreateTenantAsync/CreateUserAsync) are documented placeholders that do not yet persist real
/// Identity rows, so a full command-pipeline test (permission checks included) is not yet possible
/// for any module - this test instead proves the Organization/Employee persistence layer this item
/// built (schema, migrations, tenant isolation, cross-module Guid references) actually round-trips.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class OrgUnitIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // TestFixture already registered IConfiguration (with ConnectionStrings:Database pointing at
        // the Testcontainers instance) earlier in InitializeAsync - reuse the same instance here.
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<OrganizationDbContext>(configuration, OrganizationDbContext.SchemaName);
        services.AddModuleDbContext<EmployeeDbContext>(configuration, EmployeeDbContext.SchemaName);
    }

    [Fact]
    public async Task CreateOrgUnit_AndAssignManagerEmployee_PersistsAndIsTenantIsolated()
    {
        var organizationDb = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var employeeDb = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        await organizationDb.Database.MigrateAsync();
        await employeeDb.Database.MigrateAsync();

        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        using var scope = TenantContextSetter!.BeginScope(tenantId);

        // Seed the system hierarchy levels for this tenant (as DataSeeder does for real tenants).
        var typeResults = OrgUnitTypeCatalog.CreateAll(tenantId);
        foreach (var result in typeResults)
        {
            result.IsSuccess.ShouldBeTrue();
            organizationDb.OrgUnitTypes.Add(result.Value);
        }

        await organizationDb.SaveChangesAsync();

        var companyTypeId = typeResults.Single(r => r.Value.Code == OrgUnitTypeCodes.Company).Value.Id;

        // Create the root org unit (Company level) + its Company settings row.
        var unitResult = OrgUnit.Create(tenantId, companyTypeId, parentId: null, name: "Headquarters");
        unitResult.IsSuccess.ShouldBeTrue();
        var unit = unitResult.Value;
        organizationDb.OrgUnits.Add(unit);

        var companyResult = Company.Create(tenantId, unit.Id);
        companyResult.IsSuccess.ShouldBeTrue();
        organizationDb.Companies.Add(companyResult.Value);

        await organizationDb.SaveChangesAsync();

        // Create the manager employee (Employee module - separate schema, separate DbContext) and
        // assign them as the unit's manager via a plain cross-module Guid, no EF FK between schemas.
        var employeeResult = EmployeeEntity.Create(
            tenantId, new EmployeeInfo("Ada", "Yönetici", HireDate: new DateOnly(2020, 1, 1)), userId: null, orgUnitId: unit.Id);
        employeeResult.IsSuccess.ShouldBeTrue();
        var employee = employeeResult.Value;
        employeeDb.Employees.Add(employee);
        await employeeDb.SaveChangesAsync();

        unit.AssignManager(employee.Id);
        await organizationDb.SaveChangesAsync();

        // Assert: re-read through a fresh query (still within the same tenant scope) and confirm the
        // manager assignment, the Company link, and the OrgUnitType FK all persisted correctly.
        var reloadedUnit = await organizationDb.OrgUnits.AsNoTracking().SingleAsync(u => u.Id == unit.Id);
        reloadedUnit.Name.ShouldBe("Headquarters");
        reloadedUnit.TypeId.ShouldBe(companyTypeId);
        reloadedUnit.ManagerEmployeeId.ShouldBe(employee.Id);
        reloadedUnit.ParentId.ShouldBeNull();

        var reloadedCompany = await organizationDb.Companies.AsNoTracking().SingleAsync(c => c.OrgUnitId == unit.Id);
        reloadedCompany.TenantId.ShouldBe(tenantId);

        var reloadedEmployee = await employeeDb.Employees.AsNoTracking().SingleAsync(e => e.Id == employee.Id);
        reloadedEmployee.OrgUnitId.ShouldBe(unit.Id);
        reloadedEmployee.Status.ShouldBe(EmployeeStatus.Active);

        // Tenant isolation: switching to a different tenant must see none of this data (ModuleDbContext's
        // global query filter, not an explicit Where clause).
        using (TenantContextSetter!.BeginScope(otherTenantId))
        {
            var visibleToOtherTenant = await organizationDb.OrgUnits.AnyAsync(u => u.Id == unit.Id);
            visibleToOtherTenant.ShouldBeFalse();

            var employeeVisibleToOtherTenant = await employeeDb.Employees.AnyAsync(e => e.Id == employee.Id);
            employeeVisibleToOtherTenant.ShouldBeFalse();
        }
    }
}
