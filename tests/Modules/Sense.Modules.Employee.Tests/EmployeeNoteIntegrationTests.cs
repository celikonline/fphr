using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Notes;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Contracts.Storage;
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
/// Employee notes (E7) against PostgreSQL: create, filter by importance and employee, update, delete.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class EmployeeNoteIntegrationTests : TestFixture
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
        services.AddSingleton(Substitute.For<IFileStorage>());
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task Filter_by_importance_and_employee()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var hireDate = new DateOnly(2025, 3, 1);
        var first = (await ExecuteCommandAsync(new CreateEmployeeCommand("Bir", "Çalışan", null, null, null, null, hireDate, null, null, null))).Value;
        var second = (await ExecuteCommandAsync(new CreateEmployeeCommand("İki", "Çalışan", null, null, null, null, hireDate, null, null, null))).Value;

        var high = await ExecuteCommandAsync(new CreateEmployeeNoteCommand(first.Id, "Uyarı", "Geç kaldı", EmployeeNoteImportance.High));
        await ExecuteCommandAsync(new CreateEmployeeNoteCommand(first.Id, "Bilgi", null, EmployeeNoteImportance.Low));
        await ExecuteCommandAsync(new CreateEmployeeNoteCommand(second.Id, "Başka", "x", EmployeeNoteImportance.High));

        high.Value.AuthorName.ShouldBe("HR");
        (await ExecuteQueryAsync(new ListEmployeeNotesQuery(first.Id, null))).Value.TotalCount.ShouldBe(2);
        (await ExecuteQueryAsync(new ListEmployeeNotesQuery(null, EmployeeNoteImportance.High))).Value.TotalCount.ShouldBe(2);
        var both = (await ExecuteQueryAsync(new ListEmployeeNotesQuery(first.Id, EmployeeNoteImportance.High))).Value;
        both.Items.Single().Id.ShouldBe(high.Value.Id);

        (await ExecuteCommandAsync(new UpdateEmployeeNoteCommand(high.Value.Id, "Güncel", "y", EmployeeNoteImportance.Normal))).Value.Importance.ShouldBe(EmployeeNoteImportance.Normal);
        (await ExecuteCommandAsync(new DeleteEmployeeNoteCommand(high.Value.Id))).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new ListEmployeeNotesQuery(first.Id, null))).Value.TotalCount.ShouldBe(1);
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
