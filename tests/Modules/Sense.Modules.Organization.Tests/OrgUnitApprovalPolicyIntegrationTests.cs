using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Api;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Api;
using Sense.Modules.Organization.Application;
using Sense.Modules.Organization.Application.ApprovalPolicies;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Contracts.Events;
using Sense.Modules.Organization.Domain.OrgUnits;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Outbox;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Organization.Tests;

/// <summary>
/// Org unit approval policy slot end to end: GET/PUT use cases through Dispatcher (validation, permission, unit of work),
/// IOrgApprovalPolicyResolver inheritance, and OrgUnitApprovalPolicyChanged going outbox → OutboxProcessor → IEventBus.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class OrgUnitApprovalPolicyIntegrationTests : TestFixture
{
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly RecordingHandler _handler = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new OrganizationModule().AddModule(services, configuration);
        new EmployeeModule().AddModule(services, configuration);

        _permissions.HasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(_permissions);
        services.AddSingleton(Substitute.For<IDataScopeService>());
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
        services.AddSingleton<IIntegrationEventHandler<OrgUnitApprovalPolicyChanged>>(_handler);
    }

    protected override async Task OnDatabaseReadyAsync()
    {
        await ServiceProvider!.GetRequiredService<OrganizationDbContext>().Database.MigrateAsync();
        await ServiceProvider!.GetRequiredService<EmployeeDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task SetAndGet_UpsertsClearsAndInherits_AndPublishesChangesThroughOutbox()
    {
        var tenantId = Guid.NewGuid();
        var userId = SetUser(tenantId);
        var (root, _, department, team) = await SeedTreeAsync(tenantId);
        var (p1, p2, p3, p4, p5) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        (await Set(root.Id, (ApprovalRequestTypes.Leave, p1), (ApprovalRequestTypes.Advance, p2))).IsSuccess.ShouldBeTrue();
        (await Set(department.Id, (ApprovalRequestTypes.Leave, p3))).IsSuccess.ShouldBeTrue();

        var teamView = (await ExecuteQueryAsync(new GetOrgUnitApprovalPoliciesQuery(team.Id))).Value;
        teamView.ShouldBe(
        [
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Leave, null, p3, department.Id),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Advance, null, p2, root.Id),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Expense, null, null, null),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Overtime, null, null, null),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Requisition, null, null, null),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.AssetAssignment, null, null, null),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.AttendancePunch, null, null, null),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.PayslipObjection, null, null, null),
            new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.PunchCorrection, null, null, null),
        ]);

        var own = await Set(team.Id, (ApprovalRequestTypes.Leave, p4));
        own.Value[0].ShouldBe(new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Leave, p4, p4, null));
        (await Set(team.Id, (ApprovalRequestTypes.Leave, p4))).IsSuccess.ShouldBeTrue();

        var cleared = await Set(department.Id, (ApprovalRequestTypes.Leave, null));
        cleared.Value[0].ShouldBe(new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Leave, null, p1, root.Id));
        cleared.Value[1].ShouldBe(new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Advance, null, p2, root.Id));

        (await Set(root.Id, (ApprovalRequestTypes.Leave, p5))).IsSuccess.ShouldBeTrue();
        (await ExecuteQueryAsync(new GetOrgUnitApprovalPoliciesQuery(department.Id))).Value[0]
            .ShouldBe(new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Leave, null, p5, root.Id));
        (await ExecuteQueryAsync(new GetOrgUnitApprovalPoliciesQuery(team.Id))).Value[0]
            .ShouldBe(new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Leave, p4, p4, null));

        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var rows = await db.ApprovalPolicyAssignments.AsNoTracking().OrderBy(a => a.RequestType).ToListAsync();
        rows.Select(a => (a.OrgUnitId, a.RequestType, a.ApprovalPolicyId)).ShouldBe(
            [(root.Id, ApprovalRequestTypes.Advance, p2), (root.Id, ApprovalRequestTypes.Leave, p5), (team.Id, ApprovalRequestTypes.Leave, p4)],
            ignoreOrder: true);

        var eventType = EventTypeRegistry.NameOf(typeof(OrgUnitApprovalPolicyChanged));
        eventType.ShouldBe("Organization.OrgUnitApprovalPolicyChanged");
        (await db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type == eventType)).ShouldBe(6);

        using (var scope = ServiceProvider!.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor<OrganizationDbContext>>().ProcessAsync();
        }

        _handler.Received.Select(e => (e.OrgUnitId, e.RequestType, e.ApprovalPolicyId)).ShouldBe(
        [
            (root.Id, ApprovalRequestTypes.Leave, (Guid?)p1),
            (root.Id, ApprovalRequestTypes.Advance, p2),
            (department.Id, ApprovalRequestTypes.Leave, p3),
            (team.Id, ApprovalRequestTypes.Leave, p4),
            (department.Id, ApprovalRequestTypes.Leave, null),
            (root.Id, ApprovalRequestTypes.Leave, p5),
        ], ignoreOrder: true);
        _handler.Received.ShouldAllBe(e => e.TenantId == tenantId && e.ActorUserId == userId);
        (await db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type == eventType && m.ProcessedAt == null)).ShouldBe(0);
    }

    [Fact]
    public async Task Resolver_WalksActiveAncestors_FirstAssignmentWins_NullWhenNone()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var (root, branch, department, team) = await SeedTreeAsync(tenantId);
        var (rootPolicy, branchPolicy, teamPolicy) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Set(root.Id, (ApprovalRequestTypes.Leave, rootPolicy), (ApprovalRequestTypes.Expense, rootPolicy));
        await Set(branch.Id, (ApprovalRequestTypes.Leave, branchPolicy));
        await Set(team.Id, (ApprovalRequestTypes.Expense, teamPolicy));

        var resolver = ServiceProvider!.GetRequiredService<IOrgApprovalPolicyResolver>();

        (await resolver.ResolveAsync(tenantId, team.Id, ApprovalRequestTypes.Leave)).ShouldBe(new ApprovalPolicyResolution(branchPolicy, branch.Id, true));
        (await resolver.ResolveAsync(tenantId, team.Id, ApprovalRequestTypes.Expense)).ShouldBe(new ApprovalPolicyResolution(teamPolicy, team.Id, false));
        (await resolver.ResolveAsync(tenantId, root.Id, ApprovalRequestTypes.Leave)).ShouldBe(new ApprovalPolicyResolution(rootPolicy, root.Id, false));
        (await resolver.ResolveAsync(tenantId, team.Id, ApprovalRequestTypes.Overtime)).ShouldBeNull();
        (await resolver.ResolveAsync(tenantId, team.Id, "VisaDocument")).ShouldBeNull();
        (await resolver.ResolveAsync(tenantId, team.Id, "not a type")).ShouldBeNull();
        (await resolver.ResolveAsync(tenantId, Guid.NewGuid(), ApprovalRequestTypes.Leave)).ShouldBeNull();
        (await resolver.ResolveAsync(Guid.NewGuid(), team.Id, ApprovalRequestTypes.Leave)).ShouldBeNull();

        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        (await db.OrgUnits.SingleAsync(u => u.Id == branch.Id)).Deactivate().IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync();

        (await resolver.ResolveAsync(tenantId, department.Id, ApprovalRequestTypes.Leave)).ShouldBe(new ApprovalPolicyResolution(rootPolicy, root.Id, true));
        (await resolver.ResolveAsync(tenantId, branch.Id, ApprovalRequestTypes.Leave)).ShouldBe(new ApprovalPolicyResolution(rootPolicy, root.Id, true));
        (await ExecuteQueryAsync(new GetOrgUnitApprovalPoliciesQuery(team.Id))).Value[0]
            .ShouldBe(new OrgUnitApprovalPolicyDto(ApprovalRequestTypes.Leave, null, rootPolicy, root.Id));

        var inactive = await Set(branch.Id, (ApprovalRequestTypes.Leave, Guid.NewGuid()));
        inactive.Error.Code.ShouldBe(OrganizationErrors.ApprovalAssignmentOrgUnitInactive);
    }

    [Fact]
    public async Task Set_RejectsInvalidInput_UnknownUnit_AndRequiresManagePermission()
    {
        var tenantId = Guid.NewGuid();
        SetUser(tenantId);
        var (root, _, _, _) = await SeedTreeAsync(tenantId);

        await Should.ThrowAsync<ValidationException>(() => Set(root.Id, ("VisaDocument", Guid.NewGuid())));
        await Should.ThrowAsync<ValidationException>(() => Set(root.Id, (ApprovalRequestTypes.Leave, Guid.NewGuid()), (ApprovalRequestTypes.Leave, null)));
        await Should.ThrowAsync<ValidationException>(() => Set(root.Id, (ApprovalRequestTypes.Leave, Guid.Empty)));
        await Should.ThrowAsync<ValidationException>(() => ExecuteCommandAsync(new SetOrgUnitApprovalPoliciesCommand(root.Id, null!)));

        (await Set(Guid.NewGuid(), (ApprovalRequestTypes.Leave, Guid.NewGuid()))).Error.Type.ShouldBe(ErrorType.NotFound);
        (await ExecuteQueryAsync(new GetOrgUnitApprovalPoliciesQuery(Guid.NewGuid()))).Error.Code.ShouldBe(OrganizationErrors.OrgUnitNotFound);

        _permissions.HasAsync(Arg.Any<Guid>(), OrganizationPermissions.ApprovalAssignmentsManageKey, Arg.Any<CancellationToken>()).Returns(false);
        var forbidden = await Set(root.Id, (ApprovalRequestTypes.Leave, Guid.NewGuid()));
        forbidden.Error.Type.ShouldBe(ErrorType.Forbidden);
        (await ExecuteQueryAsync(new GetOrgUnitApprovalPoliciesQuery(root.Id))).IsSuccess.ShouldBeTrue();
        (await ServiceProvider!.GetRequiredService<OrganizationDbContext>().ApprovalPolicyAssignments.AsNoTracking().AnyAsync()).ShouldBeFalse();

        OrganizationPermissions.All.ShouldContain(p => p.Key == OrganizationPermissions.ApprovalAssignmentsManageKey);
    }

    private Task<Result<IReadOnlyList<OrgUnitApprovalPolicyDto>>> Set(Guid orgUnitId, params (string Type, Guid? PolicyId)[] items) =>
        ExecuteCommandAsync(new SetOrgUnitApprovalPoliciesCommand(
            orgUnitId,
            items.Select(i => new OrgUnitApprovalPolicyAssignmentInput(i.Type, i.PolicyId)).ToList()));

    private async Task<(OrgUnit Root, OrgUnit Branch, OrgUnit Department, OrgUnit Team)> SeedTreeAsync(Guid tenantId)
    {
        var db = ServiceProvider!.GetRequiredService<OrganizationDbContext>();
        var types = OrgUnitTypeCatalog.CreateAll(tenantId).Select(r => r.Value).ToList();
        db.OrgUnitTypes.AddRange(types);
        var typeId = types.ToDictionary(t => t.Code, t => t.Id);

        var root = OrgUnit.Create(tenantId, typeId[OrgUnitTypeCodes.Company], null, "Acme").Value;
        var branch = OrgUnit.Create(tenantId, typeId[OrgUnitTypeCodes.Branch], root.Id, "İstanbul").Value;
        var department = OrgUnit.Create(tenantId, typeId[OrgUnitTypeCodes.Department], branch.Id, "Bilgi İşlem").Value;
        var team = OrgUnit.Create(tenantId, typeId[OrgUnitTypeCodes.Title], department.Id, "Bilgi İşlem 1").Value;
        db.OrgUnits.AddRange(root, branch, department, team);
        await db.SaveChangesAsync();
        return (root, branch, department, team);
    }

    private Guid SetUser(Guid tenantId)
    {
        var userId = Guid.NewGuid();
        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: userId, EmployeeId: null, Email: "hr@test.local", DisplayName: "HR",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        TenantContextSetter!.BeginScope(tenantId);
        return userId;
    }

    private sealed class RecordingHandler : IIntegrationEventHandler<OrgUnitApprovalPolicyChanged>
    {
        public List<OrgUnitApprovalPolicyChanged> Received { get; } = [];

        public Task Handle(OrgUnitApprovalPolicyChanged integrationEvent, CancellationToken cancellationToken)
        {
            Received.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
