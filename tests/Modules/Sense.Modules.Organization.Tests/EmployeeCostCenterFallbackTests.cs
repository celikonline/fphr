using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Domain.CostCenters;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Lookups;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeCostCenterFallbackTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var config = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        services.AddModuleDbContext<OrganizationDbContext>(config, OrganizationDbContext.SchemaName);
    }
    protected override Task OnDatabaseReadyAsync() => ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task DirectThenOverrideThenNearestAncestor_WithExplicitTenantIsolation()
    {
        var tenant = Guid.NewGuid(); var foreignTenant = Guid.NewGuid();
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var employeeIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var directId = Guid.NewGuid(); Guid overrideId; Guid nearestId; Guid ancestorId;
        Guid companyId; Guid departmentId; Guid titleId;
        using (TenantContextSetter!.BeginScope(tenant))
        {
            var types = OrgUnitTypeCatalog.CreateAll(tenant).Select(r => r.Value).ToList(); db.OrgUnitTypes.AddRange(types);
            var centers = Enumerable.Range(1, 3).Select(i => CostCenter.Create(tenant, i.ToString(), "Center " + i).Value).ToList(); db.CostCenters.AddRange(centers);
            overrideId = centers[0].Id; nearestId = centers[1].Id; ancestorId = centers[2].Id;
            var company = OrgUnit.Create(tenant, types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id, null, "Company").Value;
            company.AssignCostCenter(ancestorId);
            var department = OrgUnit.Create(tenant, types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id, company.Id, "Department").Value;
            department.AssignCostCenter(nearestId);
            var title = OrgUnit.Create(tenant, types.Single(t => t.Code == OrgUnitTypeCodes.Title).Id, department.Id, "Title").Value;
            db.OrgUnits.AddRange(company, department, title);
            companyId = company.Id; departmentId = department.Id; titleId = title.Id;
            db.EmployeeCostCenterOverrides.AddRange(EmployeeCostCenterOverride.Create(tenant, employeeIds[0], overrideId), EmployeeCostCenterOverride.Create(tenant, employeeIds[1], overrideId));
            await db.SaveChangesAsync();
        }
        Guid foreignCenterId;
        using (TenantContextSetter!.BeginScope(foreignTenant))
        {
            var center = CostCenter.Create(foreignTenant, "F", "Foreign").Value; foreignCenterId = center.Id;
            db.CostCenters.Add(center); db.EmployeeCostCenterOverrides.Add(EmployeeCostCenterOverride.Create(foreignTenant, employeeIds[1], center.Id)); await db.SaveChangesAsync();
        }
        var direct = Substitute.For<IEmployeeCostCenterLookup>();
        direct.GetAsync(tenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid> { [employeeIds[0]] = directId });
        direct.GetAsync(foreignTenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid>());
        var employees = Substitute.For<IEmployeeLookup>();
        employees.GetOrgUnitIdsAsync(tenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid> { [employeeIds[1]] = titleId, [employeeIds[2]] = titleId, [employeeIds[3]] = companyId });
        employees.GetOrgUnitIdsAsync(foreignTenant, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid>());
        var reader = new EmployeeCostCenterReader(direct, db, employees);
        using (TenantContextSetter!.BeginScope(foreignTenant))
        {
            var mapping = await reader.GetEmployeeCostCenterMappingAsync(tenant, employeeIds);
            mapping[employeeIds[0]].ShouldBe(directId); mapping[employeeIds[1]].ShouldBe(overrideId);
            mapping[employeeIds[2]].ShouldBe(nearestId); mapping[employeeIds[3]].ShouldBe(ancestorId);
            mapping.ContainsKey(employeeIds[4]).ShouldBeFalse();
            var foreign = await reader.GetEmployeeCostCenterMappingAsync(foreignTenant, employeeIds);
            foreign.Count.ShouldBe(1); foreign[employeeIds[1]].ShouldBe(foreignCenterId);
        }
    }
    [Fact]
    public async Task EmptyInput_ReturnsEmptyWithoutOrganizationLookup()
    {
        var direct = Substitute.For<IEmployeeCostCenterLookup>();
        direct.GetAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, Guid>());
        var employees = Substitute.For<IEmployeeLookup>();
        var reader = new EmployeeCostCenterReader(direct, ServiceProvider!.GetRequiredService<OrganizationDbContext>(), employees);
        (await reader.GetEmployeeCostCenterMappingAsync(Guid.NewGuid(), [])).ShouldBeEmpty();
        await employees.DidNotReceiveWithAnyArgs().GetOrgUnitIdsAsync(default, default!, default);
    }
}
