using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Application.Union;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
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
/// Union memberships (Faz 1a E5b, 11.3.03): the union name is stored as ciphertext and never in the clear, an employee
/// has at most one open membership, and the dates are validated against the membership start.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class UnionMembershipIntegrationTests : TestFixture
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
    public async Task UnionMembership_IsEncryptedAtRestAndOnlyOneStaysOpen()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var hireDate = new DateOnly(2020, 1, 15);
        var employee = await ExecuteCommandAsync(new CreateEmployeeCommand("Sendika", "Üyesi", null, null, null, null, hireDate, null, null, null));
        var employeeId = employee.Value.Id;

        var added = await ExecuteCommandAsync(new AddUnionMembershipCommand(
            employeeId,
            new UnionMembershipInput(
                "Türk Metal Sendikası",
                new DateOnly(2021, 3, 1),
                UnionCoverageType.Member,
                BusinessLineCode: "13",
                EmployerNotifiedDate: new DateOnly(2021, 3, 10))));

        added.IsSuccess.ShouldBeTrue();
        var membership = added.Value.Single();
        membership.UnionName.ShouldBe("Türk Metal Sendikası");
        membership.IsCurrent.ShouldBeTrue();
        membership.EmployerNotifiedDate.ShouldBe(new DateOnly(2021, 3, 10));

        // Special-category data: the stored column is ciphertext, never the plain name.
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();
        var stored = await db.Set<EmployeeUnionMembership>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        stored.ProtectedUnionName.ShouldNotBe("Türk Metal Sendikası");
        stored.ProtectedUnionName.ShouldNotBeNullOrWhiteSpace();

        // A second open membership is rejected while one is still running.
        (await ExecuteCommandAsync(new AddUnionMembershipCommand(
            employeeId, new UnionMembershipInput("Başka Sendika", new DateOnly(2022, 1, 1), UnionCoverageType.Member))))
            .Error.Code.ShouldBe(EmployeeDomainErrors.UnionMembershipAlreadyOpen);

        // End dates cannot precede the membership date.
        (await ExecuteCommandAsync(new UpdateUnionMembershipCommand(
            employeeId,
            membership.Id,
            new UnionMembershipInput("Türk Metal Sendikası", new DateOnly(2021, 3, 1), UnionCoverageType.Member, EndDate: new DateOnly(2020, 12, 31)))))
            .Error.Code.ShouldBe(EmployeeDomainErrors.UnionEndDateBeforeMembership);

        // Ending the first membership frees the slot for the next one.
        var ended = await ExecuteCommandAsync(new UpdateUnionMembershipCommand(
            employeeId,
            membership.Id,
            new UnionMembershipInput("Türk Metal Sendikası", new DateOnly(2021, 3, 1), UnionCoverageType.Member, EndDate: new DateOnly(2023, 6, 30))));
        ended.Value.Single().IsCurrent.ShouldBeFalse();

        var second = await ExecuteCommandAsync(new AddUnionMembershipCommand(
            employeeId,
            new UnionMembershipInput("Birleşik Metal-İş", new DateOnly(2023, 7, 1), UnionCoverageType.SolidarityDues, IsUnionOfficer: true, OfficerSince: new DateOnly(2024, 1, 1))));
        second.IsSuccess.ShouldBeTrue();
        second.Value.Count.ShouldBe(2);
        second.Value[0].UnionName.ShouldBe("Birleşik Metal-İş", "the timeline is newest first");
        second.Value[0].IsUnionOfficer.ShouldBeTrue();

        var listed = (await ExecuteQueryAsync(new ListUnionMembershipsQuery(employeeId))).Value;
        listed.Count.ShouldBe(2);
        listed.Single(m => m.IsCurrent).UnionName.ShouldBe("Birleşik Metal-İş");

        var deleted = await ExecuteCommandAsync(new DeleteUnionMembershipCommand(employeeId, second.Value[0].Id));
        deleted.Value.Count.ShouldBe(1);
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
