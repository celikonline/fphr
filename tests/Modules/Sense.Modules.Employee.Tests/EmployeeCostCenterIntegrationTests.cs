using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Api.Controllers;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Application.CostCenters;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeCostCenterIntegrationTests : TestFixture
{
    private readonly FakeDataScope _scope = new();
    private bool _canUpdate = true;

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);
        var permissions = Substitute.For<IPermissionService>();
        permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<string>(1) != EmployeePermissions.EmployeesUpdateKey || _canUpdate);
        services.AddSingleton(permissions);
        services.AddSingleton<IDataScopeService>(_scope);
        services.AddSingleton<IAccessSettingsService, AllAccessSettingsEnabled>();
        services.AddSingleton<Sense.Modules.Identity.Contracts.IUserAccountLookup, FakeUserAccountLookup>();
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task AssignReloadUpdateAndClear_PreservePositionManagerAndProfile()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(result => result.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var company = OrgUnit.Create(tenantId, types.Single(type => type.Code == OrgUnitTypeCodes.Company).Id, null, "Örnek Şirket").Value;
        var department = OrgUnit.Create(tenantId, types.Single(type => type.Code == OrgUnitTypeCodes.Department).Id, company.Id, "İnsan Kaynakları").Value;
        var position = OrgUnit.Create(tenantId, types.Single(type => type.Code == OrgUnitTypeCodes.Title).Id, department.Id, "İnsan Kaynakları Direktörü").Value;
        db.OrgUnits.AddRange(company, department, position);
        await db.SaveChangesAsync();
        var boss = await CreateEmployee();
        var employee = (await ExecuteCommandAsync(new CreateEmployeeCommand("Ayşe", "Yılmaz", null, "ayse@example.com", null, null,
            new DateOnly(2020, 1, 1), null, position.Id, boss.Id))).Value;
        var centerId = (await ExecuteCommandAsync(new CreateCostCenterCommand("10200", "İnsan Kaynakları"))).Value.Id;

        var assigned = await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, centerId));
        assigned.IsSuccess.ShouldBeTrue();
        assigned.Value.CostCenterCode.ShouldBe("10200");
        assigned.Value.CostCenterName.ShouldBe("İnsan Kaynakları");
        assigned.Value.CostCenterIsActive.ShouldBe(true);
        assigned.Value.TitleName.ShouldBe(employee.TitleName);
        assigned.Value.OrgUnitId.ShouldBe(position.Id);
        assigned.Value.ManagerEmployeeId.ShouldBe(boss.Id);
        assigned.Value.HireDate.ShouldBe(employee.HireDate);
        assigned.Value.WorkEmail.ShouldBe(employee.WorkEmail);
        (await ExecuteQueryAsync(new GetEmployeeQuery(employee.Id))).Value.CostCenterId.ShouldBe(centerId);

        var updated = await ExecuteCommandAsync(new UpdateEmployeeCommand(employee.Id, employee.FirstName, employee.LastName,
            null, employee.WorkEmail, "personal@example.com", null, employee.HireDate, employee.BirthDate, position.Id, boss.Id));
        updated.Value.CostCenterId.ShouldBe(centerId);
        var reader = ServiceProvider!.GetRequiredService<IEmployeeCostCenterReader>();
        var mapping = await reader.GetEmployeeCostCenterMappingAsync(tenantId, [employee.Id, boss.Id]);
        mapping[employee.Id].ShouldBe(centerId);
        mapping.ContainsKey(boss.Id).ShouldBeFalse();
        (await reader.GetEmployeeCostCenterMappingAsync(Guid.NewGuid(), [employee.Id])).ShouldBeEmpty();
        (await reader.GetEmployeeCostCenterMappingAsync(tenantId, [])).ShouldBeEmpty();

        var cleared = await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, null));
        cleared.Value.CostCenterId.ShouldBeNull();
        cleared.Value.CostCenterName.ShouldBeNull();
        cleared.Value.OrgUnitId.ShouldBe(position.Id);
        (await ExecuteQueryAsync(new GetEmployeeQuery(employee.Id))).Value.CostCenterId.ShouldBeNull();
        (await reader.GetEmployeeCostCenterMappingAsync(tenantId, [employee.Id])).ShouldBeEmpty();
    }

    [Fact]
    public async Task InactiveCenter_PreservesExistingAssignmentButRejectsNewAssignments()
    {
        SetUser(Guid.NewGuid());
        var employee = await CreateEmployee();
        var other = await CreateEmployee();
        var centerId = (await ExecuteCommandAsync(new CreateCostCenterCommand("10200", "İnsan Kaynakları"))).Value.Id;
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, centerId))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new SetCostCenterActiveCommand(centerId, false))).IsSuccess.ShouldBeTrue();
        var reloaded = (await ExecuteQueryAsync(new GetEmployeeQuery(employee.Id))).Value;
        reloaded.CostCenterId.ShouldBe(centerId);
        reloaded.CostCenterIsActive.ShouldBe(false);
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, centerId))).IsSuccess.ShouldBeTrue();
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(other.Id, centerId))).Error.Code.ShouldBe(EmployeeErrors.CostCenterInactive);
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, null))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task RejectsForeignTenantCentersForeignEmployeesOutOfScopeAndMissingPermission()
    {
        var foreignTenant = Guid.NewGuid();
        SetUser(foreignTenant);
        var foreignCenter = (await ExecuteCommandAsync(new CreateCostCenterCommand("10200", "İnsan Kaynakları"))).Value.Id;
        var foreignEmployee = await CreateEmployee();
        SetUser(Guid.NewGuid());
        var employee = await CreateEmployee();
        var center = (await ExecuteCommandAsync(new CreateCostCenterCommand("10200", "İnsan Kaynakları"))).Value.Id;
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, foreignCenter))).Error.Code.ShouldBe(EmployeeErrors.CostCenterNotFound);
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, Guid.NewGuid()))).Error.Code.ShouldBe(EmployeeErrors.CostCenterNotFound);
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(foreignEmployee.Id, center))).IsFailure.ShouldBeTrue();
        _scope.Visible = new HashSet<Guid>();
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, center))).Error.Code.ShouldBe(ErrorCodes.OutOfScope);
        _scope.Visible = null;
        _canUpdate = false;
        (await ExecuteCommandAsync(new AssignEmployeeCostCenterCommand(employee.Id, center))).Error.Code.ShouldBe(ErrorCodes.Forbidden);
        _canUpdate = true;
        (await ExecuteQueryAsync(new GetEmployeeQuery(employee.Id))).Value.CostCenterId.ShouldBeNull();
    }

    [Fact]
    public void Request_MissingPropertyIsRejectedAndExplicitNullClears()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<EmployeeCostCenterRequest>("{}", options));
        JsonSerializer.Deserialize<EmployeeCostCenterRequest>("{\"costCenterId\":null}", options)!.CostCenterId.ShouldBeNull();
    }

    private async Task<EmployeeDetailDto> CreateEmployee()
    {
        var result = await ExecuteCommandAsync(new CreateEmployeeCommand("Örnek", "Çalışan", null, null, null, null,
            new DateOnly(2020, 1, 1), null, null, null));
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private void SetUser(Guid tenantId)
    {
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: Guid.NewGuid(), EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
    }
}
