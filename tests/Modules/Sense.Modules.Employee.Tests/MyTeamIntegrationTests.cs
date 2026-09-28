using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// FG-007 "Ekibim" against real PostgreSQL: IEmployeeLookup.GetDirectReportIdsAsync and GET employees/me/team return only the
/// caller's direct reports - never another manager's reports, indirect reports or terminated employees.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class MyTeamIntegrationTests : TestFixture
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);
        services.AddSingleton<IPermissionService, GrantAllPermissions>();
        services.AddSingleton<IDataScopeService>(new FakeDataScope());
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<IUserAccountLookup>(new FakeUserAccountLookup());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private sealed record Team(Guid ManagerA, Guid ManagerB, Guid Ayse, Guid Cem, Guid Deniz, Guid Elif, Guid Gone);

    [Fact]
    public async Task MyTeam_ReturnsOnlyTheCallersDirectReports_NeverAnotherManagersOrIndirectOrTerminated()
    {
        var tenantId = Guid.NewGuid();
        var team = await SeedTeamAsync(tenantId);

        ActAs(tenantId, team.ManagerA);
        var mine = await ExecuteQueryAsync(new GetMyTeamQuery());

        mine.IsSuccess.ShouldBeTrue();
        mine.Value.Select(m => m.EmployeeId).ShouldBe([team.Ayse, team.Cem]);
        mine.Value.Select(m => m.EmployeeId).ShouldNotContain(team.Deniz);
        var ayse = mine.Value[0];
        ayse.DisplayName.ShouldBe("Ayşe Demir");
        ayse.DepartmentName.ShouldBe("Sales");
        ayse.WorkEmail.ShouldBe("ayse@acme.test");

        ActAs(tenantId, team.ManagerB);
        (await ExecuteQueryAsync(new GetMyTeamQuery())).Value.Select(m => m.EmployeeId).ShouldBe([team.Deniz]);
    }

    [Fact]
    public async Task MyTeam_IsEmpty_ForAnEmployeeWithoutReports_AndForACallerWithoutALinkedEmployee()
    {
        var tenantId = Guid.NewGuid();
        var team = await SeedTeamAsync(tenantId);

        ActAs(tenantId, team.Cem);
        (await ExecuteQueryAsync(new GetMyTeamQuery())).Value.ShouldBeEmpty();

        ActAs(tenantId, employeeId: null);
        (await ExecuteQueryAsync(new GetMyTeamQuery())).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetDirectReportIds_IsTenantScoped_DirectOnly_AndOrderedByName()
    {
        var tenantId = Guid.NewGuid();
        var team = await SeedTeamAsync(tenantId);
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var lookup = ServiceProvider!.GetRequiredService<IEmployeeLookup>();

        (await lookup.GetDirectReportIdsAsync(tenantId, team.ManagerA, TestContext.Current.CancellationToken)).ShouldBe([team.Ayse, team.Cem]);
        (await lookup.GetDirectReportIdsAsync(tenantId, team.Ayse, TestContext.Current.CancellationToken)).ShouldBe([team.Elif]);
        (await lookup.GetDirectReportIdsAsync(Guid.NewGuid(), team.ManagerA, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await lookup.GetDirectReportIdsAsync(tenantId, Guid.NewGuid(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    /// <summary>A: Cem, Ayşe (Sales) and a terminated report; B: Deniz; Ayşe manages Elif (indirect for A).</summary>
    private async Task<Team> SeedTeamAsync(Guid tenantId)
    {
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var sales = await SeedSalesUnitAsync(tenantId);
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        var managerA = NewEmployee(tenantId, "Ahmet", "Yılmaz");
        var managerB = NewEmployee(tenantId, "Burak", "Kaya");
        var cem = NewEmployee(tenantId, "Cem", "Öz", managerId: managerA.Id);
        var ayse = NewEmployee(tenantId, "Ayşe", "Demir", managerId: managerA.Id, orgUnitId: sales, workEmail: "ayse@acme.test");
        var deniz = NewEmployee(tenantId, "Deniz", "Ak", managerId: managerB.Id);
        var elif = NewEmployee(tenantId, "Elif", "Can", managerId: ayse.Id);
        var gone = NewEmployee(tenantId, "Ali", "Eski", managerId: managerA.Id);
        gone.SetStatus(EmployeeStatus.Terminated);
        db.Employees.AddRange(managerA, managerB, cem, ayse, deniz, elif, gone);
        await db.SaveChangesAsync();

        return new Team(managerA.Id, managerB.Id, ayse.Id, cem.Id, deniz.Id, elif.Id, gone.Id);
    }

    private static Domain.Employees.Employee NewEmployee(
        Guid tenantId, string firstName, string lastName, Guid? managerId = null, Guid? orgUnitId = null, string? workEmail = null) =>
        Domain.Employees.Employee.Create(
            tenantId, new EmployeeInfo(firstName, lastName, null, workEmail, null, null, null, null), null, orgUnitId, managerId).Value;

    private async Task<Guid> SeedSalesUnitAsync(Guid tenantId)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(t => t.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var root = OrgUnit.Create(tenantId, types.Single(t => t.Code == OrgUnitTypeCodes.Company).Id, null, "Acme").Value;
        var sales = OrgUnit.Create(tenantId, types.Single(t => t.Code == OrgUnitTypeCodes.Department).Id, root.Id, "Sales").Value;
        db.OrgUnits.AddRange(root, sales);
        await db.SaveChangesAsync();
        return sales.Id;
    }

    private void ActAs(Guid tenantId, Guid? employeeId)
    {
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: employeeId, Email: "manager@test.local", DisplayName: "Manager",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
}
