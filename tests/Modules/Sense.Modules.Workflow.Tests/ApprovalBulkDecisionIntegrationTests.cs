using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Api;
using Sense.Modules.Workflow.Application;
using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence.Audit;
using Sense.Shared.Kernel.Results;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// FG-008 against real PostgreSQL with the whole Workflow module and the full command pipeline (validation,
/// AuthorizationBehaviour, UnitOfWork, outbox): decide-bulk commits every task on its own - someone else's task fails
/// with AssigneeMismatch while the caller's tasks are decided (Mobile channel stored) and publish their events - and
/// the history query lists only the caller's decisions. Cross-module ports (permissions, directory, roles) are faked.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ApprovalBulkDecisionIntegrationTests : TestFixture
{
    private const string DecidePermission = "workflow.inbox.decide";
    private const string ViewPermission = "workflow.inbox.view";

    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly IEmployeeDirectory _employeeDirectory = Substitute.For<IEmployeeDirectory>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;
        new WorkflowModule().AddModule(services, configuration);

        _employeeDirectory.GetContactsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        services.AddSingleton(_permissions);
        services.AddSingleton(_employeeDirectory);
        services.AddSingleton(Substitute.For<IDataScopeService>());
        services.AddSingleton(Substitute.For<IAccessSettingsService>());
        services.AddSingleton(Substitute.For<IRoleAssignmentLookup>());
        services.AddSingleton(Substitute.For<IEmployeeLookup>());
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Guid ActAsEmployee(Guid tenantId, Guid employeeId, params string[] grantedPermissions)
    {
        var userId = Guid.NewGuid();
        foreach (var permission in grantedPermissions)
        {
            _permissions.HasAsync(userId, permission, Arg.Any<CancellationToken>()).Returns(true);
        }

        ((CurrentUserAccessor)CurrentUser!).Set(new AuthenticatedUser(
            UserId: userId, EmployeeId: employeeId, Email: "approver@test.local", DisplayName: "Approver",
            Roles: new HashSet<string>(StringComparer.Ordinal), IsPlatformAdmin: false,
            ImpersonatorUserId: null, ApiKeyId: null, CorrelationId: null, IpAddress: "127.0.0.1"));
        return userId;
    }

    /// <summary>A one-step request waiting on <paramref name="approverEmployeeId"/>; returns (requestId, taskId).</summary>
    private async Task<(Guid RequestId, Guid TaskId)> SeedPendingAsync(Guid tenantId, Guid approverEmployeeId, string requestType = "Leave")
    {
        using var scope = ServiceProvider!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var request = ApprovalRequest.Submit(
            tenantId, requestType, Guid.NewGuid(), Guid.NewGuid(), [new ApprovalTaskSeed(1, approverEmployeeId, null, true)],
            null, null, null, false, true, DateTime.UtcNow, null, null).Value;
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return (request.Id, request.Tasks.Single().Id);
    }

    private async Task<ApprovalRequest> ReloadAsync(Guid requestId)
    {
        using var scope = ServiceProvider!.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>().GetByIdAsync(requestId, Ct)).Value;
    }

    [Fact]
    public async Task DecideBulk_SomeoneElsesTaskFails_TheCallersTasksAreDecidedAndCommittedIndependently()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver = Guid.NewGuid();
        var userId = ActAsEmployee(tenantId, approver, DecidePermission);
        var mine1 = await SeedPendingAsync(tenantId, approver);
        var foreign = await SeedPendingAsync(tenantId, Guid.NewGuid());
        var mine2 = await SeedPendingAsync(tenantId, approver);

        var result = await Dispatcher!.Send(
            new DecideApprovalTasksBulkCommand(
                [mine1.TaskId, foreign.TaskId, mine2.TaskId], approver, ApprovalOutcome.Approved, null, ApprovalChannel.Mobile,
                "203.0.113.9", "SenseMobile/1.0"),
            Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(r => r.TaskId).ShouldBe([mine1.TaskId, foreign.TaskId, mine2.TaskId]);
        result.Value.Select(r => r.Succeeded).ShouldBe([true, false, true]);
        result.Value[1].Error!.Code.ShouldBe(WorkflowErrors.AssigneeMismatch);

        foreach (var mine in new[] { mine1, mine2 })
        {
            var decided = await ReloadAsync(mine.RequestId);
            decided.Status.ShouldBe(ApprovalRequestStatus.Approved);
            var task = decided.Tasks.Single();
            task.DecidedByEmployeeId.ShouldBe(approver);
            task.DecisionChannel.ShouldBe(ApprovalChannel.Mobile);
            decided.History.ShouldContain(h => h.EventType == ApprovalHistoryEventType.Decided && h.Channel == ApprovalChannel.Mobile);
        }

        var untouched = await ReloadAsync(foreign.RequestId);
        untouched.Status.ShouldBe(ApprovalRequestStatus.Pending);
        untouched.Tasks.Single().Status.ShouldBe(ApprovalTaskStatus.Pending);

        // Each successful task committed its own ApprovalCompleted outbox row; the failed one wrote nothing.
        using var verifyScope = ServiceProvider!.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var completedPayloads = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains("ApprovalCompleted"))
            .Select(m => m.Payload)
            .ToListAsync(Ct);
        completedPayloads.Count.ShouldBe(2);
        completedPayloads.ShouldNotContain(p => p.Contains(foreign.RequestId.ToString()));

        // The child scope ran as the caller: the audit trail of the decided request names them, not "system".
        var auditActors = await db.Set<AuditLogEntry>().AsNoTracking()
            .Where(e => e.ResourceId == mine1.RequestId.ToString() && e.Action == AuditActions.Update)
            .Select(e => e.ActorUserId)
            .ToListAsync(Ct);
        auditActors.ShouldNotBeEmpty();
        auditActors.ShouldAllBe(a => a == userId);
    }

    [Fact]
    public async Task DecideBulk_WithoutDecidePermission_IsForbidden_AndDecidesNothing()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver = Guid.NewGuid();
        ActAsEmployee(tenantId, approver, ViewPermission);
        var mine = await SeedPendingAsync(tenantId, approver);

        var result = await Dispatcher!.Send(
            new DecideApprovalTasksBulkCommand([mine.TaskId], approver, ApprovalOutcome.Approved, null, ApprovalChannel.Web, null, null), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
        (await ReloadAsync(mine.RequestId)).Status.ShouldBe(ApprovalRequestStatus.Pending);
    }

    [Fact]
    public async Task History_ListsOnlyTheCallersDecisions_NewestFirst_WithTypeAndDateFilters()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        ActAsEmployee(tenantId, approver);
        var leave = await SeedPendingAsync(tenantId, approver, "Leave");
        var overtime = await SeedPendingAsync(tenantId, approver, "Overtime");
        var stillPending = await SeedPendingAsync(tenantId, approver, "Leave");
        var colleagues = await SeedPendingAsync(tenantId, colleague, "Leave");

        ActAsEmployee(tenantId, colleague, DecidePermission);
        (await Dispatcher!.Send(
            new DecideApprovalTaskCommand(colleagues.TaskId, colleague, ApprovalOutcome.Approved, null, ApprovalChannel.Web, null, null), Ct))
            .IsSuccess.ShouldBeTrue();

        ActAsEmployee(tenantId, approver, DecidePermission, ViewPermission);
        (await Dispatcher.Send(
            new DecideApprovalTaskCommand(leave.TaskId, approver, ApprovalOutcome.Approved, null, ApprovalChannel.Web, null, null), Ct))
            .IsSuccess.ShouldBeTrue();
        (await Dispatcher.Send(
            new DecideApprovalTaskCommand(overtime.TaskId, approver, ApprovalOutcome.Rejected, "Bütçe yok", ApprovalChannel.Mobile, null, null), Ct))
            .IsSuccess.ShouldBeTrue();

        var all = await Dispatcher.Query(new ListApprovalHistoryQuery(null, null, null), Ct);

        all.IsSuccess.ShouldBeTrue();
        all.Value.TotalCount.ShouldBe(2);
        all.Value.Items.Select(i => i.TaskId).ShouldBe([overtime.TaskId, leave.TaskId]);
        all.Value.Items.ShouldNotContain(i => i.TaskId == stillPending.TaskId || i.TaskId == colleagues.TaskId);
        var rejected = all.Value.Items[0];
        rejected.Outcome.ShouldBe(ApprovalOutcome.Rejected);
        rejected.Comment.ShouldBe("Bütçe yok");
        rejected.RequestStatus.ShouldBe(ApprovalRequestStatus.Rejected);
        rejected.RequestType.ShouldBe("Overtime");
        all.Value.Items[1].Outcome.ShouldBe(ApprovalOutcome.Approved);

        var leaveOnly = await Dispatcher.Query(new ListApprovalHistoryQuery(null, null, "Leave"), Ct);
        leaveOnly.Value.Items.Select(i => i.TaskId).ShouldBe([leave.TaskId]);

        var today = DateOnly.FromDateTime(rejected.DecidedAt);
        (await Dispatcher.Query(new ListApprovalHistoryQuery(today, today, null), Ct)).Value.TotalCount.ShouldBe(2);
        (await Dispatcher.Query(new ListApprovalHistoryQuery(today.AddDays(1), null, null), Ct)).Value.TotalCount.ShouldBe(0);

        var secondPage = await Dispatcher.Query(new ListApprovalHistoryQuery(null, null, null, Page: 2, PageSize: 1), Ct);
        secondPage.Value.Items.Select(i => i.TaskId).ShouldBe([leave.TaskId]);
        secondPage.Value.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task History_WithoutViewPermission_IsForbidden()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        ActAsEmployee(tenantId, Guid.NewGuid(), DecidePermission);

        var result = await Dispatcher!.Query(new ListApprovalHistoryQuery(null, null, null), Ct);

        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    /// <summary>ApprovalChannel is stored as a string column, so appending Mobile needs no migration.</summary>
    [Fact]
    public void WorkflowModel_HasNoPendingChanges_AfterAddingTheMobileChannel()
    {
        using var scope = ServiceProvider!.CreateScope();
        scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Database.HasPendingModelChanges().ShouldBeFalse();
    }
}
