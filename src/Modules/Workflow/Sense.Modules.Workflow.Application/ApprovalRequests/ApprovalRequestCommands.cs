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
    IEmailTokenGenerator tokenGenerator,
    IWorkflowFrontendLinks frontendLinks,
    IApprovalMailer mailer,
    IEmailDecisionTokenRepository tokenRepository,
    IEmployeeDirectory employeeDirectory,
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

        await ApprovalDecisionEffects.SendCurrentTaskEmailAsync(
            request, tenant.TenantId, tokenGenerator, frontendLinks, mailer, tokenRepository, employeeDirectory, clock, cancellationToken);

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

/// <summary>Approves/rejects a pending task the caller is the assignee of (directly, or via a matching fallback role queue).</summary>
[RequiresPermission("workflow.inbox.decide")]
public sealed record DecideApprovalTaskCommand(
    Guid TaskId, Guid DecidingEmployeeId, ApprovalOutcome Outcome, string? Comment, ApprovalChannel Channel, string? IpAddress, string? UserAgent) : ICommand;

public sealed class DecideApprovalTaskValidator : AbstractValidator<DecideApprovalTaskCommand>
{
    public DecideApprovalTaskValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.DecidingEmployeeId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class DecideApprovalTaskHandler(
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IRoleAssignmentLookup roleAssignments,
    IEmailTokenGenerator tokenGenerator,
    IWorkflowFrontendLinks frontendLinks,
    IApprovalMailer mailer,
    IEmailDecisionTokenRepository tokenRepository,
    IEmployeeDirectory employeeDirectory,
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
            task.StepNumber, command.DecidingEmployeeId, command.Outcome, command.Comment, wasOverride: false,
            command.Channel, command.IpAddress, command.UserAgent, clock.GetUtcNow().UtcDateTime);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        await ApprovalDecisionEffects.ApplyPostDecideEffectsAsync(
            request, tenant.TenantId, task.StepNumber, tokenGenerator, frontendLinks, mailer, tokenRepository, employeeDirectory, outbox, clock, cancellationToken);

        return Result.Success();
    }
}

/// <summary>Decides a pending task on someone else's behalf (Admin override) - skips the assignee/fallback-role check DecideApprovalTaskCommand enforces.</summary>
[RequiresPermission("workflow.request.override")]
public sealed record OverrideDecideApprovalTaskCommand(
    Guid TaskId, Guid DecidingEmployeeId, ApprovalOutcome Outcome, string? Comment, ApprovalChannel Channel, string? IpAddress, string? UserAgent) : ICommand;

public sealed class OverrideDecideApprovalTaskValidator : AbstractValidator<OverrideDecideApprovalTaskCommand>
{
    public OverrideDecideApprovalTaskValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.DecidingEmployeeId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class OverrideDecideApprovalTaskHandler(
    ITenantContext tenant,
    IApprovalRequestRepository requests,
    IEmailTokenGenerator tokenGenerator,
    IWorkflowFrontendLinks frontendLinks,
    IApprovalMailer mailer,
    IEmailDecisionTokenRepository tokenRepository,
    IEmployeeDirectory employeeDirectory,
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

        var decided = request.Decide(
            task.StepNumber, command.DecidingEmployeeId, command.Outcome, command.Comment, wasOverride: true,
            command.Channel, command.IpAddress, command.UserAgent, clock.GetUtcNow().UtcDateTime);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        await ApprovalDecisionEffects.ApplyPostDecideEffectsAsync(
            request, tenant.TenantId, task.StepNumber, tokenGenerator, frontendLinks, mailer, tokenRepository, employeeDirectory, outbox, clock, cancellationToken);

        return Result.Success();
    }
}

/// <summary>Decides a task via the single-use link in the "you have a pending approval" email. Public, unauthenticated.</summary>
public sealed record DecideByEmailTokenCommand(string Token, ApprovalOutcome Outcome, string? Comment, string? IpAddress, string? UserAgent) : ICommand;

public sealed class DecideByEmailTokenValidator : AbstractValidator<DecideByEmailTokenCommand>
{
    public DecideByEmailTokenValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(WorkflowLimits.CommentMaxLength);
    }
}

public sealed class DecideByEmailTokenHandler(
    IEmailDecisionTokenRepository tokenRepository,
    IApprovalRequestRepository requests,
    IEmailTokenGenerator tokenGenerator,
    IWorkflowFrontendLinks frontendLinks,
    IApprovalMailer mailer,
    IEmployeeDirectory employeeDirectory,
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
            task.StepNumber, decidingEmployeeId, command.Outcome, command.Comment, wasOverride: false,
            ApprovalChannel.Email, command.IpAddress, command.UserAgent, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        await ApprovalDecisionEffects.ApplyPostDecideEffectsAsync(
            request, request.TenantId, task.StepNumber, tokenGenerator, frontendLinks, mailer, tokenRepository, employeeDirectory, outbox, clock, cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// Post-decision effects shared by DecideApprovalTaskCommand, OverrideDecideApprovalTaskCommand and
/// DecideByEmailTokenCommand (and, for the first task, SubmitApprovalRequestCommand): mint+email a decision token
/// for whichever task is now current, or - once the request reaches a terminal state - enqueue the integration event.
/// </summary>
internal static class ApprovalDecisionEffects
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(7);

    public static async Task ApplyPostDecideEffectsAsync(
        ApprovalRequest request,
        Guid tenantId,
        int decidedStepNumber,
        IEmailTokenGenerator tokenGenerator,
        IWorkflowFrontendLinks frontendLinks,
        IApprovalMailer mailer,
        IEmailDecisionTokenRepository tokenRepository,
        IEmployeeDirectory employeeDirectory,
        IIntegrationEventOutbox outbox,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.Status == ApprovalRequestStatus.Pending)
        {
            await SendCurrentTaskEmailAsync(request, tenantId, tokenGenerator, frontendLinks, mailer, tokenRepository, employeeDirectory, clock, cancellationToken);
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

    /// <summary>Mints and emails a single-use decision token for the request's current task - only when it has a concrete assignee.</summary>
    public static async Task SendCurrentTaskEmailAsync(
        ApprovalRequest request,
        Guid tenantId,
        IEmailTokenGenerator tokenGenerator,
        IWorkflowFrontendLinks frontendLinks,
        IApprovalMailer mailer,
        IEmailDecisionTokenRepository tokenRepository,
        IEmployeeDirectory employeeDirectory,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.CurrentTask?.ApproverEmployeeId is not { } approverEmployeeId)
        {
            return;
        }

        var contacts = await employeeDirectory.GetContactsAsync(tenantId, [approverEmployeeId], cancellationToken);
        var contact = contacts.FirstOrDefault();
        var email = contact?.WorkEmail ?? contact?.PersonalEmail;
        if (contact is null || string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var rawToken = tokenGenerator.NewToken();
        var expiresAt = clock.GetUtcNow().UtcDateTime.Add(TokenLifetime);
        tokenRepository.Add(new EmailDecisionToken(request.CurrentTask.Id, tenantId, tokenGenerator.Hash(rawToken), expiresAt));

        var approverName = $"{contact.FirstName} {contact.LastName}".Trim();
        await mailer.SendDecisionRequestAsync(email, approverName, request.RequestType, frontendLinks.DecisionLink(rawToken), expiresAt, cancellationToken);
    }
}
