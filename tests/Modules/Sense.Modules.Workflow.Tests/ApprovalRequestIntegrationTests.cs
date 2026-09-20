using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Tests.Shared.Fixtures;
using Shouldly;
using Xunit;

namespace Sense.Modules.Workflow.Tests;

/// <summary>
/// Integration tests (real PostgreSQL via Testcontainers, see TestFixture) for the approval request state machine:
/// submit, sequential decisions, required-vs-optional step rejection, policy snapshot semantics, and audit trail.
/// Goes directly through WorkflowDbContext/the module's repositories/domain rather than Dispatcher.Send - a full
/// command-pipeline test (permission checks, cross-module resolution) is not yet wired up for this fixture (see
/// OrgUnitIntegrationTests for the same rationale). Each write/decide step uses a fresh DI scope (a fresh
/// WorkflowDbContext) so reads genuinely round-trip through the database rather than returning an already-tracked
/// in-memory instance.
///
/// Note: an earlier version of these tests needed a manual EF change-tracker fix-up after Decide()/Cancel() on an
/// already-persisted request, because ApprovalTask/ApprovalHistoryEntry/EmailDecisionToken's client-generated Guid
/// ids (Guid.CreateVersion7()) made EF's new-vs-existing heuristic for entities discovered via navigation fixup
/// classify a newly-appended row as Modified instead of Added - which flags every scalar (including TenantId) as
/// changed and trips AuditTenantInterceptor's immutability check. Fixed at the source via
/// `.Property(x => x.Id).ValueGeneratedNever()` on those three configurations in WorkflowDbContext.cs (same pattern
/// already used for ApprovalPolicyStep.StepNumber); no workaround needed here anymore.
/// </summary>
[Collection("PostgreSQL")]
[Trait("Category", "Integration")]
public sealed class ApprovalRequestIntegrationTests : TestFixture
{
    private static readonly TimeSpan DateTolerance = TimeSpan.FromMilliseconds(5);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = (IConfiguration)services.First(s => s.ServiceType == typeof(IConfiguration)).ImplementationInstance!;

        services.AddModuleDbContext<WorkflowDbContext>(configuration, WorkflowDbContext.SchemaName);
        services.AddScoped<IApprovalPolicyRepository, ApprovalPolicyRepository>();
        services.AddScoped<IApprovalRequestRepository, ApprovalRequestRepository>();
        services.AddScoped<IEmailDecisionTokenRepository, EmailDecisionTokenRepository>();
    }

    protected override async Task OnDatabaseReadyAsync() =>
        await ServiceProvider!.GetRequiredService<WorkflowDbContext>().Database.MigrateAsync();

    [Fact]
    public async Task Submit_TwoSequentialRequiredSteps_AdvancesThenApproves()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver1 = Guid.NewGuid();
        var approver2 = Guid.NewGuid();
        Guid requestId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = writeScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            IReadOnlyList<ApprovalTaskSeed> steps =
            [
                new ApprovalTaskSeed(1, approver1, null, true),
                new ApprovalTaskSeed(2, approver2, null, true),
            ];

            var submitted = ApprovalRequest.Submit(
                tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps,
                null, null, null, false, true, DateTime.UtcNow, null, null);
            submitted.IsSuccess.ShouldBeTrue();
            requests.Add(submitted.Value);
            await db.SaveChangesAsync();
            requestId = submitted.Value.Id;
        }

        // Reload fresh from the DB: task 2 cannot be decided while task 1 is still the current (pending) step.
        using (var decideScope1 = ServiceProvider!.CreateScope())
        {
            var db = decideScope1.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = decideScope1.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;

            var prematureDecision = reloaded.Decide(2, approver2, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow);
            prematureDecision.IsFailure.ShouldBeTrue();
            prematureDecision.Error.Code.ShouldBe(WorkflowDomainErrors.TaskNotCurrent);

            var decide1 = reloaded.Decide(1, approver1, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow);
            decide1.IsSuccess.ShouldBeTrue();
            reloaded.CurrentStepNumber.ShouldBe(2);
            reloaded.Status.ShouldBe(ApprovalRequestStatus.Pending);
            await db.SaveChangesAsync();
        }

        using (var decideScope2 = ServiceProvider!.CreateScope())
        {
            var db = decideScope2.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = decideScope2.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;
            reloaded.CurrentStepNumber.ShouldBe(2);

            var decide2 = reloaded.Decide(2, approver2, ApprovalOutcome.Approved, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow);
            decide2.IsSuccess.ShouldBeTrue();
            reloaded.Status.ShouldBe(ApprovalRequestStatus.Approved);
            reloaded.CompletedAt.ShouldNotBeNull();
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Decide_RequiredStepRejected_SetsRequestRejected_LeavesRemainingTaskPending()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver1 = Guid.NewGuid();
        var approver2 = Guid.NewGuid();
        Guid requestId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = writeScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            IReadOnlyList<ApprovalTaskSeed> steps =
            [
                new ApprovalTaskSeed(1, approver1, null, true),
                new ApprovalTaskSeed(2, approver2, null, true),
            ];

            var submitted = ApprovalRequest.Submit(
                tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps, null, null, null, false, true, DateTime.UtcNow, null, null).Value;
            requests.Add(submitted);
            await db.SaveChangesAsync();
            requestId = submitted.Id;
        }

        using (var decideScope = ServiceProvider!.CreateScope())
        {
            var db = decideScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = decideScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var request = (await requests.GetByIdAsync(requestId)).Value;

            var decide = request.Decide(1, approver1, ApprovalOutcome.Rejected, "declined", false, ApprovalChannel.Web, null, null, DateTime.UtcNow);
            decide.IsSuccess.ShouldBeTrue();
            request.Status.ShouldBe(ApprovalRequestStatus.Rejected);
            request.CurrentStepNumber.ShouldBeNull();
            await db.SaveChangesAsync();
        }

        using (var verifyScope = ServiceProvider!.CreateScope())
        {
            var requests = verifyScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;

            reloaded.Status.ShouldBe(ApprovalRequestStatus.Rejected);
            reloaded.Tasks.Single(t => t.StepNumber == 1).Status.ShouldBe(ApprovalTaskStatus.Rejected);

            // Deliberate design choice: a required-step reject does NOT cancel the remaining task(s).
            reloaded.Tasks.Single(t => t.StepNumber == 2).Status.ShouldBe(ApprovalTaskStatus.Pending);
        }
    }

    [Fact]
    public async Task ListInboxTasks_ExcludesTasksOfCancelledAndRejectedRequests()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver = Guid.NewGuid();
        var secondApprover = Guid.NewGuid();
        Guid pendingId, cancelledId, rejectedId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = writeScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();

            ApprovalRequest Submit(params ApprovalTaskSeed[] steps) =>
                ApprovalRequest.Submit(tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps, null, null, null, false, true, DateTime.UtcNow, null, null).Value;

            var pending = Submit(new ApprovalTaskSeed(1, approver, null, true));
            var cancelled = Submit(new ApprovalTaskSeed(1, approver, null, true));
            var rejected = Submit(new ApprovalTaskSeed(1, secondApprover, null, true), new ApprovalTaskSeed(2, approver, null, true));
            cancelled.Cancel(Guid.NewGuid(), DateTime.UtcNow).IsSuccess.ShouldBeTrue();
            rejected.Decide(1, secondApprover, ApprovalOutcome.Rejected, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow).IsSuccess.ShouldBeTrue();

            requests.Add(pending);
            requests.Add(cancelled);
            requests.Add(rejected);
            await db.SaveChangesAsync();
            (pendingId, cancelledId, rejectedId) = (pending.Id, cancelled.Id, rejected.Id);
        }

        using var readScope = ServiceProvider!.CreateScope();
        var repository = readScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();

        var inbox = await repository.ListInboxTasksAsync(approver, []);

        inbox.Select(t => t.ApprovalRequestId).ShouldBe([pendingId]);
        inbox.ShouldNotContain(t => t.ApprovalRequestId == cancelledId || t.ApprovalRequestId == rejectedId);
    }

    [Fact]
    public async Task Decide_OptionalStepRejected_DoesNotTerminateRequest_AdvancesNormally()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approver1 = Guid.NewGuid();
        var approver2 = Guid.NewGuid();
        Guid requestId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = writeScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            IReadOnlyList<ApprovalTaskSeed> steps =
            [
                new ApprovalTaskSeed(1, approver1, null, false), // optional step
                new ApprovalTaskSeed(2, approver2, null, true),
            ];

            var submitted = ApprovalRequest.Submit(
                tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps, null, null, null, false, true, DateTime.UtcNow, null, null).Value;
            requests.Add(submitted);
            await db.SaveChangesAsync();
            requestId = submitted.Id;
        }

        using (var decideScope = ServiceProvider!.CreateScope())
        {
            var db = decideScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = decideScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var request = (await requests.GetByIdAsync(requestId)).Value;

            var decide = request.Decide(1, approver1, ApprovalOutcome.Rejected, null, false, ApprovalChannel.Web, null, null, DateTime.UtcNow);
            decide.IsSuccess.ShouldBeTrue();

            // An optional step's reject does not terminate the request - it still advances normally.
            request.Status.ShouldBe(ApprovalRequestStatus.Pending);
            request.CurrentStepNumber.ShouldBe(2);
            await db.SaveChangesAsync();
        }

        using (var verifyScope = ServiceProvider!.CreateScope())
        {
            var requests = verifyScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;

            reloaded.Status.ShouldBe(ApprovalRequestStatus.Pending);
            reloaded.CurrentStepNumber.ShouldBe(2);
            reloaded.Tasks.Single(t => t.StepNumber == 1).Status.ShouldBe(ApprovalTaskStatus.Rejected);
            reloaded.Tasks.Single(t => t.StepNumber == 2).Status.ShouldBe(ApprovalTaskStatus.Pending);
        }
    }

    [Fact]
    public async Task PolicyEdit_AfterSubmission_DoesNotAffectInFlightRequest()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approverId = Guid.NewGuid();
        Guid policyId;
        Guid requestId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var policies = writeScope.ServiceProvider.GetRequiredService<IApprovalPolicyRepository>();
            var requests = writeScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();

            var policy = ApprovalPolicy.Create(tenantId, "Leave", "Original Policy", true, [(approverId, true)]).Value;
            policies.Add(policy);
            await db.SaveChangesAsync();
            policyId = policy.Id;

            IReadOnlyList<ApprovalTaskSeed> steps = [new ApprovalTaskSeed(1, approverId, null, true)];
            var submitted = ApprovalRequest.Submit(
                tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps,
                policyId, policy.Name, null, false, true, DateTime.UtcNow, null, null).Value;
            requests.Add(submitted);
            await db.SaveChangesAsync();
            requestId = submitted.Id;
        }

        // Mutate/replace the source policy's steps after the request was already submitted.
        using (var editScope = ServiceProvider!.CreateScope())
        {
            var db = editScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var policies = editScope.ServiceProvider.GetRequiredService<IApprovalPolicyRepository>();
            var policy = (await policies.GetByIdAsync(policyId)).Value;

            policy.Rename("Renamed Policy").IsSuccess.ShouldBeTrue();
            policy.ReplaceSteps([(Guid.NewGuid(), true), (Guid.NewGuid(), true)]).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var verifyScope = ServiceProvider!.CreateScope())
        {
            var requests = verifyScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;

            // Snapshot semantics: the already-submitted request is unaffected by the later policy edit.
            reloaded.PolicyNameAtSubmission.ShouldBe("Original Policy");
            reloaded.Tasks.Count.ShouldBe(1);
            reloaded.Tasks.Single().ApproverEmployeeId.ShouldBe(approverId);
        }
    }

    [Fact]
    public async Task Submit_AndDecide_PopulateAuditFieldsAndAppendHistory()
    {
        var tenantId = Guid.NewGuid();
        using var tenantScope = TenantContextSetter!.BeginScope(tenantId);
        var approverId = Guid.NewGuid();
        var submittedAt = DateTime.UtcNow;
        Guid requestId;
        Guid taskId;

        using (var writeScope = ServiceProvider!.CreateScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = writeScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            IReadOnlyList<ApprovalTaskSeed> steps = [new ApprovalTaskSeed(1, approverId, null, true)];

            var submitted = ApprovalRequest.Submit(
                tenantId, "Leave", Guid.NewGuid(), Guid.NewGuid(), steps,
                null, null, null, false, true, submittedAt, "203.0.113.5", "TestAgent/1.0").Value;
            requests.Add(submitted);
            await db.SaveChangesAsync();
            requestId = submitted.Id;
            taskId = submitted.Tasks.Single().Id;
        }

        using (var readScope = ServiceProvider!.CreateScope())
        {
            var requests = readScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;

            reloaded.SubmittedAt.ShouldBe(submittedAt, DateTolerance);
            reloaded.SubmittedIpAddress.ShouldBe("203.0.113.5");
            reloaded.SubmittedUserAgent.ShouldBe("TestAgent/1.0");
        }

        var decidedAt = DateTime.UtcNow;
        using (var decideScope = ServiceProvider!.CreateScope())
        {
            var db = decideScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var requests = decideScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var request = (await requests.GetByIdAsync(requestId)).Value;

            var decide = request.Decide(
                1, approverId, ApprovalOutcome.Approved, "looks good", false, ApprovalChannel.Email, "198.51.100.7", "MailClient/2.0", decidedAt);
            decide.IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync();
        }

        using (var verifyScope = ServiceProvider!.CreateScope())
        {
            var requests = verifyScope.ServiceProvider.GetRequiredService<IApprovalRequestRepository>();
            var reloaded = (await requests.GetByIdAsync(requestId)).Value;

            var task = reloaded.Tasks.Single(t => t.Id == taskId);
            task.DecidedByEmployeeId.ShouldBe(approverId);
            task.DecidedAt.ShouldNotBeNull();
            task.DecidedAt!.Value.ShouldBe(decidedAt, DateTolerance);
            task.DecisionChannel.ShouldBe(ApprovalChannel.Email);
            task.DecisionIpAddress.ShouldBe("198.51.100.7");
            task.DecisionUserAgent.ShouldBe("MailClient/2.0");

            reloaded.History.ShouldContain(h =>
                h.EventType == ApprovalHistoryEventType.Decided
                && h.ActorEmployeeId == approverId
                && h.StepNumber == 1
                && h.Outcome == ApprovalOutcome.Approved
                && h.Channel == ApprovalChannel.Email
                && h.IpAddress == "198.51.100.7"
                && h.UserAgent == "MailClient/2.0");
        }
    }
}
