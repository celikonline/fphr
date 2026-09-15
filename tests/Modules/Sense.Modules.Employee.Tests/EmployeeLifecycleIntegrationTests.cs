using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Application.Employees;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
// Both Domain.Employees and Contracts.Events declare a SuspensionReason enum (Contracts stays independent of Domain,
// per architecture rule) - alias the Contracts one to keep bare SuspensionReason meaning the Domain enum here.
using ContractEvents = Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Employee.Tests;

/// <summary>
/// Faz 1a status model: Draft (quick-add without a hire date) -> Active (EmployeeHired, exactly once) ->
/// Suspended/Reactivated, all going Employee's outbox -> OutboxProcessor -> IEventBus end to end; and
/// employee.sensitive.view masking/write-preservation on the personal/health fields.
/// </summary>
[Collection("PostgreSQL")]
public sealed class EmployeeLifecycleIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly RecordingHandler<ContractEvents.EmployeeHired> _hired = new();
    private readonly RecordingHandler<ContractEvents.EmployeeSuspended> _suspended = new();
    private readonly RecordingHandler<ContractEvents.EmployeeReactivated> _reactivated = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        // Every permission granted except employee.sensitive.view, which each test controls explicitly.
        _permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _permissions.HasAsync(Arg.Any<Guid>(), EmployeePermissions.SensitiveViewKey, Arg.Any<CancellationToken>()).Returns(false);
        services.AddSingleton(_permissions);

        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        // Unconfigured NSubstitute calls returning an interface (IReadOnlySet<Guid>) auto-substitute a non-null empty
        // proxy rather than null, which EmployeeReferenceValidator reads as "restricted to nothing" - null means
        // unrestricted (Company scope), so it must be stubbed explicitly.
        dataScope.GetAccessibleOrgUnitIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        dataScope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        services.AddSingleton(dataScope);
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
        services.AddSingleton(Substitute.For<IUserAccountLookup>());
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeHired>>(_hired);
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeSuspended>>(_suspended);
        services.AddSingleton<IIntegrationEventHandler<ContractEvents.EmployeeReactivated>>(_reactivated);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    private void AllowSensitiveView() =>
        _permissions.HasAsync(Arg.Any<Guid>(), EmployeePermissions.SensitiveViewKey, Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task QuickAddWithoutHireDate_IsDraft_AndSettingHireDateHiresExactlyOnce()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var db = ServiceProvider!.GetRequiredService<EmployeeDbContext>();

        var created = await ExecuteCommandAsync(new CreateEmployeeCommand("Ada", "Kovan", null, null, null, null, null, null, null, null));
        created.IsSuccess.ShouldBeTrue();
        created.Value.Status.ShouldBe(nameof(EmployeeStatus.Draft));

        var hiredEventType = EventTypeRegistry.NameOf(typeof(ContractEvents.EmployeeHired));
        (await db.OutboxMessages.CountAsync(m => m.Type == hiredEventType)).ShouldBe(0);

        // First time the hire date is set: Draft -> Active, EmployeeHired raised exactly once.
        var hireDate = new DateOnly(2026, 1, 15);
        var updated = await ExecuteCommandAsync(new UpdateEmployeeCommand(
            created.Value.Id, "Ada", "Kovan", null, null, null, null, hireDate, null, null, null));
        updated.IsSuccess.ShouldBeTrue();
        updated.Value.Status.ShouldBe(nameof(EmployeeStatus.Active));
        (await db.OutboxMessages.CountAsync(m => m.Type == hiredEventType)).ShouldBe(1);

        // A later update that keeps the hire date does not raise it again.
        await ExecuteCommandAsync(new UpdateEmployeeCommand(created.Value.Id, "Ada", "K.", null, null, null, null, hireDate, null, null, null));
        (await db.OutboxMessages.CountAsync(m => m.Type == hiredEventType)).ShouldBe(1);

        using (var scope = ServiceProvider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor<EmployeeDbContext>>().ProcessAsync();
        }

        var received = _hired.Received.ShouldHaveSingleItem();
        received.EmployeeId.ShouldBe(created.Value.Id);
        received.HireDate.ShouldBe(hireDate);
        received.LeaveAccrualStartDate.ShouldBe(hireDate);
    }

    [Fact]
    public async Task Suspend_ThenReactivate_RaisesEventsAndRejectsInvalidTransitions()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);

        var created = await ExecuteCommandAsync(new CreateEmployeeCommand(
            "Baran", "Su", null, null, null, null, new DateOnly(2022, 6, 1), null, null, null));
        var employeeId = created.Value.Id;

        var draft = await ExecuteCommandAsync(new CreateEmployeeCommand("Ceren", "Ay", null, null, null, null, null, null, null, null));
        (await ExecuteCommandAsync(new SuspendEmployeeCommand(draft.Value.Id, SuspensionReason.Other))).Error.Code.ShouldBe(EmployeeDomainErrors.SuspendInvalidStatus);

        var suspended = await ExecuteCommandAsync(new SuspendEmployeeCommand(employeeId, SuspensionReason.MilitaryService));
        suspended.IsSuccess.ShouldBeTrue();
        suspended.Value.Status.ShouldBe(nameof(EmployeeStatus.Suspended));
        suspended.Value.SuspensionReason.ShouldBe(nameof(SuspensionReason.MilitaryService));

        (await ExecuteCommandAsync(new SuspendEmployeeCommand(employeeId, SuspensionReason.Other))).Error.Code.ShouldBe(EmployeeDomainErrors.SuspendInvalidStatus);
        (await ExecuteCommandAsync(new ReactivateEmployeeCommand(draft.Value.Id))).Error.Code.ShouldBe(EmployeeDomainErrors.ReactivateNotSuspended);

        var reactivated = await ExecuteCommandAsync(new ReactivateEmployeeCommand(employeeId));
        reactivated.IsSuccess.ShouldBeTrue();
        reactivated.Value.Status.ShouldBe(nameof(EmployeeStatus.Active));
        reactivated.Value.SuspensionReason.ShouldBeNull();

        using (var scope = ServiceProvider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor<EmployeeDbContext>>().ProcessAsync();
        }

        _suspended.Received.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.EmployeeId.ShouldBe(employeeId), e => e.Reason.ShouldBe(ContractEvents.SuspensionReason.MilitaryService));
        _reactivated.Received.ShouldHaveSingleItem().EmployeeId.ShouldBe(employeeId);
    }

    [Fact]
    public async Task SensitiveFields_AreMaskedWithoutPermission_AndPreservedOnWriteEvenWhenMasked()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);

        AllowSensitiveView();
        var nationalId = Tckn.Next();
        var created = await ExecuteCommandAsync(new CreateEmployeeCommand(
            "Deniz", "Kara", nationalId, null, null, null, new DateOnly(2023, 1, 1), null, null, null,
            DisabilityDegree: DisabilityDegree.Second, BloodType: BloodType.APos, MilitaryStatus: MilitaryStatus.Completed));
        created.IsSuccess.ShouldBeTrue();
        created.Value.NationalId.ShouldBe(nationalId);
        created.Value.DisabilityDegree.ShouldBe(DisabilityDegree.Second);

        // Without the permission: masked/null on read...
        _permissions.HasAsync(Arg.Any<Guid>(), EmployeePermissions.SensitiveViewKey, Arg.Any<CancellationToken>()).Returns(false);
        var masked = await ExecuteQueryAsync(new GetEmployeeQuery(created.Value.Id));
        masked.Value.NationalId.ShouldNotBe(nationalId);
        masked.Value.NationalId.ShouldEndWith(nationalId[^4..]);
        masked.Value.DisabilityDegree.ShouldBeNull();
        masked.Value.BloodType.ShouldBeNull();
        masked.Value.MilitaryStatus.ShouldBeNull();

        // ...and a full-body update (as the web card would send back, with the masked/null values it received) does
        // NOT overwrite the real stored values - the caller cannot clear what they cannot see.
        var blindUpdate = await ExecuteCommandAsync(new UpdateEmployeeCommand(
            created.Value.Id, "Deniz", "Kara", masked.Value.NationalId, null, null, null, created.Value.HireDate, null, null, null,
            ChildCount: 2));
        blindUpdate.IsSuccess.ShouldBeTrue();
        blindUpdate.Value.ChildCount.ShouldBe(2);

        AllowSensitiveView();
        var reread = await ExecuteQueryAsync(new GetEmployeeQuery(created.Value.Id));
        reread.Value.NationalId.ShouldBe(nationalId);
        reread.Value.DisabilityDegree.ShouldBe(DisabilityDegree.Second);
        reread.Value.BloodType.ShouldBe(BloodType.APos);
        reread.Value.MilitaryStatus.ShouldBe(MilitaryStatus.Completed);
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

internal sealed class RecordingHandler<TEvent> : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public List<TEvent> Received { get; } = [];

    public Task Handle(TEvent integrationEvent, CancellationToken cancellationToken)
    {
        Received.Add(integrationEvent);
        return Task.CompletedTask;
    }
}
