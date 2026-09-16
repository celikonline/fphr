using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Subcontractors;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Subcontractors;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Legislation;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Subcontractors (Faz 1a E5b, 11.3.03 #37): the tenant catalog and the employee link the İSG headcount rule (Faz 7)
/// will read. Deactivated companies stay linkable to nobody new but keep their existing employees.
/// </summary>
[Collection("PostgreSQL")]
public sealed class SubcontractorIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        _permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(_permissions);

        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _dataScope.GetAccessibleOrgUnitIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        services.AddSingleton(_dataScope);
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
        services.AddSingleton(Substitute.For<Sense.Modules.Identity.Contracts.IUserAccountLookup>());
        services.AddSingleton(Substitute.For<ILegislationParameterReader>());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Subcontractor_CatalogAndEmployeeLink()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);

        var created = await ExecuteCommandAsync(new CreateSubcontractorCommand(new SubcontractorData(
            "  Örnek Temizlik A.Ş. ",
            TaxNumber: "1234567890",
            ContactName: "Ayşe Yılmaz",
            ContactEmail: " Info@Ornek.COM ",
            ContactPhone: "0212 555 00 00",
            ContractStartDate: new DateOnly(2025, 1, 1),
            ContractEndDate: new DateOnly(2026, 12, 31))));

        created.IsSuccess.ShouldBeTrue();
        created.Value.Name.ShouldBe("Örnek Temizlik A.Ş.");
        created.Value.ContactEmail.ShouldBe("info@ornek.com");
        created.Value.IsActive.ShouldBeTrue();

        // A contract that ends before it starts is rejected.
        (await ExecuteCommandAsync(new UpdateSubcontractorCommand(
            created.Value.Id,
            new SubcontractorData("Örnek Temizlik A.Ş.", ContractStartDate: new DateOnly(2025, 1, 1), ContractEndDate: new DateOnly(2024, 1, 1)))))
            .Error.Code.ShouldBe(EmployeeDomainErrors.SubcontractorContractEndBeforeStart);

        var hireDate = new DateOnly(2025, 3, 1);
        var employee = await ExecuteCommandAsync(new CreateEmployeeCommand("Taşeron", "Çalışanı", null, null, null, null, hireDate, null, null, null));

        var linked = await ExecuteCommandAsync(new LinkEmployeeSubcontractorCommand(employee.Value.Id, created.Value.Id));
        linked.Value.SubcontractorId.ShouldBe(created.Value.Id);
        linked.Value.SubcontractorName.ShouldBe("Örnek Temizlik A.Ş.");

        // The card keeps showing the link after a reload.
        (await ExecuteQueryAsync(new GetEmployeeQuery(employee.Value.Id))).Value.SubcontractorName.ShouldBe("Örnek Temizlik A.Ş.");

        // Deactivated companies drop out of the picker but keep their employees.
        (await ExecuteCommandAsync(new SetSubcontractorActiveCommand(created.Value.Id, Active: false))).Value.IsActive.ShouldBeFalse();
        (await ExecuteQueryAsync(new ListSubcontractorsQuery())).Value.ShouldBeEmpty();
        (await ExecuteQueryAsync(new ListSubcontractorsQuery(IncludeInactive: true))).Value.Count.ShouldBe(1);
        (await ExecuteQueryAsync(new GetEmployeeQuery(employee.Value.Id))).Value.SubcontractorId.ShouldBe(created.Value.Id);

        // Nobody new can be linked to an inactive company.
        var other = await ExecuteCommandAsync(new CreateEmployeeCommand("Başka", "Çalışan", null, null, null, null, hireDate, null, null, null));
        (await ExecuteCommandAsync(new LinkEmployeeSubcontractorCommand(other.Value.Id, created.Value.Id)))
            .Error.Code.ShouldBe(EmployeeDomainErrors.SubcontractorInactive);

        // Unlinking makes the employee the tenant's own staff again.
        (await ExecuteCommandAsync(new LinkEmployeeSubcontractorCommand(employee.Value.Id, null))).Value.SubcontractorId.ShouldBeNull();
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
