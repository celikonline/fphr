using FluentValidation;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application.ApprovalRequests;

/// <summary>
/// Starts an approval process. Internal-only: reached via <see cref="IWorkflowService"/>, not a public HTTP route -
/// the calling module (e.g. future Leave) owns its own "may this caller submit for this subject" permission check.
/// </summary>
public sealed record SubmitApprovalRequestCommand(
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    IReadOnlyList<ApprovalStepInput>? ExplicitSteps,
    string? SubmittedIpAddress,
    string? SubmittedUserAgent) : ICommand<Guid>;

public sealed class SubmitApprovalRequestValidator : AbstractValidator<SubmitApprovalRequestCommand>
{
    public SubmitApprovalRequestValidator()
    {
        RuleFor(x => x.RequestType).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.RequesterEmployeeId).NotEmpty();
    }
}

public sealed class SubmitApprovalRequestHandler(
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IApprovalPolicyRepository policies,
    IApproverResolver resolver,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : ICommandHandler<SubmitApprovalRequestCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SubmitApprovalRequestCommand command, CancellationToken cancellationToken)
    {
        IReadOnlyList<ApprovalTaskSeed> steps;
        Guid? policyId = null;
        string? policyName = null;
        Guid? resolvedFromOrgUnitId = null;
        var resolvedByInheritance = false;

        if (command.ExplicitSteps is { Count: > 0 } explicitSteps)
        {
            steps = explicitSteps.Select(s => new ApprovalTaskSeed(s.StepNumber, s.ApproverEmployeeId, null, s.IsRequired)).ToList();
        }
        else
        {
            var resolved = await resolver.ResolveAsync(tenant.TenantId, command.RequestType, command.RequesterEmployeeId, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Error;
            }

            steps = resolved.Value.Steps;
            policyId = resolved.Value.PolicyId;
            policyName = resolved.Value.PolicyName;
            resolvedFromOrgUnitId = resolved.Value.ResolvedFromOrgUnitId;
            resolvedByInheritance = resolved.Value.ResolvedByInheritance;
        }

        // Default true (direct-manager/fallback-role resolution carries no policy to read the flag from); when a
        // policy was resolved, its own visibility setting wins.
        var showCurrentApproverToRequester = true;
        if (policyId is { } resolvedPolicyId)
        {
            var policy = await policies.FindByIdAsync(resolvedPolicyId, cancellationToken);
            if (policy is not null)
            {
                showCurrentApproverToRequester = policy.ShowCurrentApproverToRequester;
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var submitted = ApprovalRequest.Submit(
            tenant.TenantId, command.RequestType, command.SubjectId, command.RequesterEmployeeId, steps,
            policyId, policyName, resolvedFromOrgUnitId, resolvedByInheritance, showCurrentApproverToRequester,
            now, command.SubmittedIpAddress, command.SubmittedUserAgent);
        if (submitted.IsFailure)
        {
            return submitted.Error;
        }

        var request = submitted.Value;
        requests.Add(request);

        outbox.Enqueue(new ApprovalRequested(tenant.TenantId, request.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId));
        ApprovalDecisionEffects.EnqueueCurrentTaskAssigned(request, tenant.TenantId, outbox);

        return request.Id;
    }
}

/// <summary>Cancels a pending approval request. Internal-only: the calling module owns its own permission check.</summary>
public sealed record CancelApprovalRequestCommand(Guid ApprovalRequestId, Guid ActorEmployeeId) : ICommand;

public sealed class CancelApprovalRequestValidator : AbstractValidator<CancelApprovalRequestCommand>
{
    public CancelApprovalRequestValidator()
    {
        RuleFor(x => x.ApprovalRequestId).NotEmpty();
        RuleFor(x => x.ActorEmployeeId).NotEmpty();
    }
}

public sealed class CancelApprovalRequestHandler(IApprovalRequestRepository requests, TimeProvider clock) : ICommandHandler<CancelApprovalRequestCommand>
{
    public async Task<Result> Handle(CancelApprovalRequestCommand command, CancellationToken cancellationToken)
    {
        var existing = await requests.GetByIdAsync(command.ApprovalRequestId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var result = existing.Value.Cancel(command.ActorEmployeeId, clock.GetUtcNow().UtcDateTime);
        return result.IsFailure ? result.Error : Result.Success();
    }
}

/// <summary>One step's new approver for <see cref="EditApprovalRequestApproversCommand"/> - identity only, the task's IsRequired flag is left untouched.</summary>
public sealed record ApprovalStepApproverEdit(int StepNumber, Guid ApproverEmployeeId);

/// <summary>
/// TK-009 "talep anında onaycı listesi düzenleme": reassigns the approver on one or more of a request's still-pending
/// tasks (the source policy/template is never touched, and the request must not have been decided/cancelled yet).
/// Public, caller-identity-dependent authorization (requester or the requester's direct manager) - checked in the
/// handler like <see cref="GetApprovalStatusQuery"/>, not a flat [RequiresPermission].
/// </summary>
public sealed record EditApprovalRequestApproversCommand(
    Guid ApprovalRequestId, Guid ActorEmployeeId, IReadOnlyList<ApprovalStepApproverEdit> Approvers) : ICommand;

public sealed class EditApprovalRequestApproversValidator : AbstractValidator<EditApprovalRequestApproversCommand>
{
    public EditApprovalRequestApproversValidator()
    {
        RuleFor(x => x.ApprovalRequestId).NotEmpty();
        RuleFor(x => x.ActorEmployeeId).NotEmpty();
        RuleFor(x => x.Approvers).NotEmpty();
        RuleForEach(x => x.Approvers).ChildRules(step =>
        {
            step.RuleFor(s => s.StepNumber).GreaterThan(0);
            step.RuleFor(s => s.ApproverEmployeeId).NotEmpty();
        });
    }
}

public sealed class EditApprovalRequestApproversHandler(
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IEmployeeLookup employeeLookup,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : ICommandHandler<EditApprovalRequestApproversCommand>
{
    public async Task<Result> Handle(EditApprovalRequestApproversCommand command, CancellationToken cancellationToken)
    {
        var loaded = await requests.GetByIdAsync(command.ApprovalRequestId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var request = loaded.Value;

        var isRequester = request.RequesterEmployeeId == command.ActorEmployeeId;
        var isManager = !isRequester
            && await employeeLookup.GetManagerEmployeeIdAsync(tenant.TenantId, request.RequesterEmployeeId, cancellationToken) == command.ActorEmployeeId;
        if (!isRequester && !isManager)
        {
            return Error.Rule(WorkflowErrors.EditApproversNotAuthorized);
        }

        foreach (var edit in command.Approvers)
        {
            if (!await employeeLookup.ExistsActiveAsync(tenant.TenantId, edit.ApproverEmployeeId, cancellationToken))
            {
                return Error.Validation(WorkflowErrors.PolicyStepApproverInvalid, (nameof(ApprovalStepApproverEdit.ApproverEmployeeId), edit.ApproverEmployeeId));
            }
        }

        var wasCurrentStepEdited = command.Approvers.Any(a => a.StepNumber == request.CurrentStepNumber);
        var edited = request.EditApprovers(
            command.Approvers.Select(a => (a.StepNumber, a.ApproverEmployeeId)).ToList(), command.ActorEmployeeId, clock.GetUtcNow().UtcDateTime);
        if (edited.IsFailure)
        {
            return edited.Error;
        }

        // The current task's assignee changed - let the newly-assigned approver know (same event Submit/Decide use).
        if (wasCurrentStepEdited)
        {
            ApprovalDecisionEffects.EnqueueCurrentTaskAssigned(request, tenant.TenantId, outbox);
        }

        return Result.Success();
    }
}

/// <summary>Approves/rejects a pending task the caller is the assignee of (directly, or via a matching fallback role queue).</summary>
[RequiresPermission("workflow.inbox.decide")]
public sealed record DecideApprovalTaskCommand(
    Guid TaskId, Guid DecidingEmployeeId, ApprovalOutcome? Outcome, string? Comment, ApprovalChannel Channel, string? IpAddress, string? UserAgent) : ICommand;

public sealed class DecideApprovalTaskValidator : AbstractValidator<DecideApprovalTaskCommand>
{
    public DecideApprovalTaskValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.DecidingEmployeeId).NotEmpty();
        RuleFor(x => x.Outcome).NotNull().IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class DecideApprovalTaskHandler(
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IRoleAssignmentLookup roleAssignments,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : ICommandHandler<DecideApprovalTaskCommand>
{
    public async Task<Result> Handle(DecideApprovalTaskCommand command, CancellationToken cancellationToken)
    {
        var loaded = await requests.GetByTaskIdAsync(command.TaskId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var request = loaded.Value;
        var task = request.Tasks.Single(t => t.Id == command.TaskId);

        var isAssignee = task.ApproverEmployeeId == command.DecidingEmployeeId;
        var isFallbackRoleMember = !isAssignee && task.FallbackRoleCode is { } roleCode &&
            await roleAssignments.EmployeeHasRoleAsync(tenant.TenantId, command.DecidingEmployeeId, roleCode, cancellationToken);
        if (!isAssignee && !isFallbackRoleMember)
        {
            return Error.Rule(WorkflowErrors.AssigneeMismatch);
        }

        var decided = request.Decide(
            task.StepNumber, command.DecidingEmployeeId, command.Outcome!.Value, command.Comment, wasOverride: false,
            command.Channel, command.IpAddress, command.UserAgent, clock.GetUtcNow().UtcDateTime);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        ApprovalDecisionEffects.ApplyPostDecideEffects(request, tenant.TenantId, task.StepNumber, outbox);

        return Result.Success();
    }
}

/// <summary>
/// FG-008: approves/rejects up to <see cref="WorkflowLimits.BulkDecisionMaxTasks"/> of the caller's pending tasks at once.
/// Every task goes through its own <see cref="DecideApprovalTaskCommand"/> (same assignee/fallback-role and process
/// rules) in its own transaction via <see cref="IIsolatedApprovalDecisionRunner"/>, so the answer is per task: a task
/// that fails (someone else's task → AssigneeMismatch, already decided, ...) neither stops nor rolls back the others.
/// Call-level problems - an empty or oversized list, a rejection without a comment - fail the whole call up front.
/// Duplicate ids are decided once (first occurrence keeps its position in the result).
/// </summary>
[RequiresPermission("workflow.inbox.decide")]
public sealed record DecideApprovalTasksBulkCommand(
    IReadOnlyList<Guid>? TaskIds, Guid DecidingEmployeeId, ApprovalOutcome? Outcome, string? Comment, ApprovalChannel Channel,
    string? IpAddress, string? UserAgent) : ICommand<IReadOnlyList<BulkDecisionTaskResult>>;

public sealed class DecideApprovalTasksBulkValidator : AbstractValidator<DecideApprovalTasksBulkCommand>
{
    public DecideApprovalTasksBulkValidator()
    {
        RuleForEach(x => x.TaskIds).NotEmpty();
        RuleFor(x => x.DecidingEmployeeId).NotEmpty();
        RuleFor(x => x.Outcome).NotNull().IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class DecideApprovalTasksBulkHandler(IIsolatedApprovalDecisionRunner runner)
    : ICommandHandler<DecideApprovalTasksBulkCommand, IReadOnlyList<BulkDecisionTaskResult>>
{
    public async Task<Result<IReadOnlyList<BulkDecisionTaskResult>>> Handle(DecideApprovalTasksBulkCommand command, CancellationToken cancellationToken)
    {
        var taskIds = (command.TaskIds ?? []).Distinct().ToList();
        if (taskIds.Count == 0)
        {
            return Error.Validation(WorkflowErrors.BulkEmpty);
        }

        if (taskIds.Count > WorkflowLimits.BulkDecisionMaxTasks)
        {
            return Error.Validation(WorkflowErrors.BulkTooManyTasks, ("max", WorkflowLimits.BulkDecisionMaxTasks));
        }

        // Same rule every task would hit - answer it once for the call instead of N identical per-task failures.
        var commentCheck = ApprovalRequest.ValidateDecisionComment(command.Outcome!.Value, command.Comment);
        if (commentCheck.IsFailure)
        {
            return commentCheck.Error;
        }

        var results = new List<BulkDecisionTaskResult>(taskIds.Count);
        foreach (var taskId in taskIds)
        {
            var decided = await runner.DecideAsync(
                new DecideApprovalTaskCommand(
                    taskId, command.DecidingEmployeeId, command.Outcome, command.Comment, command.Channel, command.IpAddress, command.UserAgent),
                cancellationToken);
            results.Add(new BulkDecisionTaskResult(taskId, decided.IsFailure ? decided.Error : null));
        }

        return Result.Success<IReadOnlyList<BulkDecisionTaskResult>>(results);
    }
}

/// <summary>Decides a pending task on someone else's behalf (Admin override) - skips the assignee/fallback-role check DecideApprovalTaskCommand enforces.</summary>
[RequiresPermission("workflow.request.override")]
public sealed record OverrideDecideApprovalTaskCommand(
    Guid TaskId, Guid DecidingEmployeeId, ApprovalOutcome? Outcome, string? Comment, ApprovalChannel Channel, string? IpAddress, string? UserAgent) : ICommand;

public sealed class OverrideDecideApprovalTaskValidator : AbstractValidator<OverrideDecideApprovalTaskCommand>
{
    public OverrideDecideApprovalTaskValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.DecidingEmployeeId).NotEmpty();
        RuleFor(x => x.Outcome).NotNull().IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class OverrideDecideApprovalTaskHandler(
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : ICommandHandler<OverrideDecideApprovalTaskCommand>
{
    public async Task<Result> Handle(OverrideDecideApprovalTaskCommand command, CancellationToken cancellationToken)
    {
        var loaded = await requests.GetByTaskIdAsync(command.TaskId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var request = loaded.Value;
        var task = request.Tasks.Single(t => t.Id == command.TaskId);

        // Override, atanan kontrolünü atlar ama kimse kendi talebini onaylayamaz (self-approval).
        if (request.RequesterEmployeeId == command.DecidingEmployeeId)
        {
            return Error.Rule(WorkflowErrors.SelfApprovalNotAllowed);
        }

        var decided = request.Decide(
            task.StepNumber, command.DecidingEmployeeId, command.Outcome!.Value, command.Comment, wasOverride: true,
            command.Channel, command.IpAddress, command.UserAgent, clock.GetUtcNow().UtcDateTime);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        ApprovalDecisionEffects.ApplyPostDecideEffects(request, tenant.TenantId, task.StepNumber, outbox);

        return Result.Success();
    }
}

/// <summary>Decides a task via the single-use link in the "you have a pending approval" email. Public, unauthenticated.</summary>
public sealed record DecideByEmailTokenCommand(string Token, ApprovalOutcome? Outcome, string? Comment, string? IpAddress, string? UserAgent) : ICommand;

public sealed class DecideByEmailTokenValidator : AbstractValidator<DecideByEmailTokenCommand>
{
    public DecideByEmailTokenValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Outcome).NotNull().IsInEnum();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class DecideByEmailTokenHandler(
    IEmailDecisionTokenRepository tokenRepository,
    IApprovalRequestRepository requests,
    IEmailTokenGenerator tokenGenerator,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : ICommandHandler<DecideByEmailTokenCommand>
{
    public async Task<Result> Handle(DecideByEmailTokenCommand command, CancellationToken cancellationToken)
    {
        var loadedToken = await tokenRepository.GetByTokenHashAsync(tokenGenerator.Hash(command.Token), cancellationToken);
        if (loadedToken.IsFailure)
        {
            return Error.NotFound(WorkflowErrors.TokenInvalid);
        }

        // Single-use token: reject a comment-less rejection before consuming it, so the user can retry with a comment.
        var commentCheck = ApprovalRequest.ValidateDecisionComment(command.Outcome!.Value, command.Comment);
        if (commentCheck.IsFailure)
        {
            return commentCheck;
        }

        var token = loadedToken.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var used = token.MarkUsed(now);
        if (used.IsFailure)
        {
            return used.Error;
        }

        var loadedRequest = await requests.GetByTaskIdAsync(token.ApprovalTaskId, cancellationToken);
        if (loadedRequest.IsFailure)
        {
            return loadedRequest.Error;
        }

        var request = loadedRequest.Value;
        var task = request.Tasks.Single(t => t.Id == token.ApprovalTaskId);

        // Someone else already decided a later step, or an override moved the request past this task since the
        // email was sent - the token is technically still unused/unexpired but no longer actionable.
        if (request.CurrentStepNumber != task.StepNumber)
        {
            return Error.Rule(WorkflowErrors.TaskNotCurrent);
        }

        // Tokens are only ever minted for a task with a concrete ApproverEmployeeId (see ApprovalDecisionEffects);
        // a null approver here would mean a stale/foreign token pointing at a fallback-role task.
        if (task.ApproverEmployeeId is not { } decidingEmployeeId)
        {
            return Error.Rule(WorkflowErrors.AssigneeMismatch);
        }

        var decided = request.Decide(
            task.StepNumber, decidingEmployeeId, command.Outcome!.Value, command.Comment, wasOverride: false,
            ApprovalChannel.Email, command.IpAddress, command.UserAgent, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        ApprovalDecisionEffects.ApplyPostDecideEffects(request, request.TenantId, task.StepNumber, outbox);

        return Result.Success();
    }
}

/// <summary>
/// FG-009: a party of the request (see <see cref="ApprovalRequestAccess"/>) leaves a comment - recorded as a
/// <see cref="ApprovalHistoryEventType.Commented"/> history entry, no separate table. Non-parties get NotFound, like the
/// status read. Raises <see cref="ApprovalRequestCommented"/> for the other parties' in-app notification.
/// </summary>
public sealed record AddApprovalRequestCommentCommand(Guid ApprovalRequestId, Guid AuthorEmployeeId, string? Text) : ICommand<Guid>;

public sealed class AddApprovalRequestCommentValidator : AbstractValidator<AddApprovalRequestCommentCommand>
{
    public AddApprovalRequestCommentValidator()
    {
        RuleFor(x => x.ApprovalRequestId).NotEmpty();
        RuleFor(x => x.AuthorEmployeeId).NotEmpty();
        RuleFor(x => x.Text).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class AddApprovalRequestCommentHandler(
    ICurrentUser currentUser,
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IRoleAssignmentLookup roleAssignments,
    IPermissionService permissions,
    IIntegrationEventOutbox outbox,
    TimeProvider clock) : ICommandHandler<AddApprovalRequestCommentCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddApprovalRequestCommentCommand command, CancellationToken cancellationToken)
    {
        var loaded = await requests.GetByIdAsync(command.ApprovalRequestId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var request = loaded.Value;
        if (!await ApprovalRequestAccess.IsPartyAsync(
                request, command.AuthorEmployeeId, currentUser.UserId, tenant.TenantId, roleAssignments, permissions, cancellationToken))
        {
            return Error.NotFound(WorkflowErrors.RequestNotFound);
        }

        var added = request.AddComment(command.AuthorEmployeeId, command.Text, clock.GetUtcNow().UtcDateTime);
        if (added.IsFailure)
        {
            return added.Error;
        }

        var recipients = await ResolveRecipientsAsync(request, command.AuthorEmployeeId, cancellationToken);
        if (recipients.Count > 0)
        {
            outbox.Enqueue(new ApprovalRequestCommented(
                tenant.TenantId, request.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId,
                command.AuthorEmployeeId, recipients));
        }

        return added.Value.Id;
    }

    /// <summary>
    /// The requester, every concrete approver/decider of the request's tasks and - while the current step waits on a
    /// role queue - that role's members; never the author.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(ApprovalRequest request, Guid authorEmployeeId, CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid> { request.RequesterEmployeeId };
        foreach (var task in request.Tasks)
        {
            if (task.ApproverEmployeeId is { } approverId)
            {
                recipients.Add(approverId);
            }

            if (task.DecidedByEmployeeId is { } deciderId)
            {
                recipients.Add(deciderId);
            }
        }

        if (request.CurrentTask is { ApproverEmployeeId: null, FallbackRoleCode: { } roleCode })
        {
            recipients.UnionWith(await roleAssignments.ListEmployeeIdsInRoleAsync(tenant.TenantId, roleCode, cancellationToken));
        }

        recipients.Remove(authorEmployeeId);
        return recipients.ToList();
    }
}

/// <summary>
/// Post-decision effects shared by DecideApprovalTaskCommand, OverrideDecideApprovalTaskCommand and
/// DecideByEmailTokenCommand (and, for the first task, SubmitApprovalRequestCommand): enqueue ApprovalTaskAssigned for
/// whichever task is now current, or - once the request reaches a terminal state - ApprovalCompleted/ApprovalRejected.
/// Notifications consumes these (e-mail + in-app); the decision token is minted at e-mail send time through
/// IApprovalDecisionLinkIssuer, not here.
/// </summary>
internal static class ApprovalDecisionEffects
{
    public static void ApplyPostDecideEffects(ApprovalRequest request, Guid tenantId, int decidedStepNumber, IIntegrationEventOutbox outbox)
    {
        if (request.Status == ApprovalRequestStatus.Pending)
        {
            EnqueueCurrentTaskAssigned(request, tenantId, outbox);
            return;
        }

        if (request.Status == ApprovalRequestStatus.Approved)
        {
            outbox.Enqueue(new ApprovalCompleted(tenantId, request.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId));
        }
        else if (request.Status == ApprovalRequestStatus.Rejected)
        {
            outbox.Enqueue(new ApprovalRejected(tenantId, request.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId, decidedStepNumber));
        }
    }

    public static void EnqueueCurrentTaskAssigned(ApprovalRequest request, Guid tenantId, IIntegrationEventOutbox outbox)
    {
        if (request.CurrentTask is not { } task)
        {
            return;
        }

        outbox.Enqueue(new ApprovalTaskAssigned(
            tenantId, request.Id, task.Id, request.RequestType, request.SubjectId, request.RequesterEmployeeId,
            task.StepNumber, task.ApproverEmployeeId, task.FallbackRoleCode));
    }
}
