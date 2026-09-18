using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Domain.ApprovalRequests;

public enum ApprovalRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

public enum ApprovalTaskStatus
{
    Pending,
    Approved,
    Rejected,
}

public enum ApprovalOutcome
{
    Approved,
    Rejected,
}

public enum ApprovalChannel
{
    Web,
    Email,
    Api,
}

public enum ApprovalHistoryEventType
{
    Submitted,
    Decided,
    Completed,
    Rejected,
    Cancelled,
}

/// <summary>One resolved step to seed an <see cref="ApprovalRequest"/> with - either a specific employee or (no policy/manager resolved) a role queue.</summary>
public sealed record ApprovalTaskSeed(int StepNumber, Guid? ApproverEmployeeId, string? FallbackRoleCode, bool IsRequired);

/// <summary>
/// The state machine owner for a single approval process instance (Kolay İK "Talep → karar" akışı). Everything needed
/// to run the process is snapshotted at <see cref="Submit"/> time from the resolved policy/steps - later edits to the
/// source <see cref="Domain.ApprovalPolicies.ApprovalPolicy"/> never affect an in-flight request.
/// </summary>
public sealed class ApprovalRequest : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<ApprovalTask> _tasks = [];
    private readonly List<ApprovalHistoryEntry> _history = [];

    private ApprovalRequest()
    {
    }

    private ApprovalRequest(
        Guid id, Guid tenantId, string requestType, Guid subjectId, Guid requesterEmployeeId,
        Guid? policyId, string? policyNameAtSubmission, Guid? resolvedFromOrgUnitId, bool resolvedByInheritance,
        bool showCurrentApproverToRequester, int stepCount, DateTime submittedAt, string? submittedIpAddress, string? submittedUserAgent)
        : base(id, tenantId)
    {
        RequestType = requestType;
        SubjectId = subjectId;
        RequesterEmployeeId = requesterEmployeeId;
        PolicyId = policyId;
        PolicyNameAtSubmission = policyNameAtSubmission;
        ResolvedFromOrgUnitId = resolvedFromOrgUnitId;
        ResolvedByInheritance = resolvedByInheritance;
        ShowCurrentApproverToRequester = showCurrentApproverToRequester;
        Status = ApprovalRequestStatus.Pending;
        StepCount = stepCount;
        CurrentStepNumber = 1;
        SubmittedAt = submittedAt;
        SubmittedIpAddress = submittedIpAddress;
        SubmittedUserAgent = submittedUserAgent;
    }

    public string RequestType { get; private set; } = string.Empty;

    /// <summary>Opaque id of whatever the consuming module's request is (e.g. a future LeaveRequest.Id) - Workflow never dereferences it.</summary>
    public Guid SubjectId { get; private set; }

    public Guid RequesterEmployeeId { get; private set; }

    /// <summary>Null when resolved via the direct-manager or fallback-role-queue default (no policy involved).</summary>
    public Guid? PolicyId { get; private set; }

    public string? PolicyNameAtSubmission { get; private set; }

    /// <summary>Org unit the policy was resolved from (own unit or an ancestor); see Organization.Contracts.ApprovalPolicyResolution.</summary>
    public Guid? ResolvedFromOrgUnitId { get; private set; }

    public bool ResolvedByInheritance { get; private set; }

    public bool ShowCurrentApproverToRequester { get; private set; }

    public ApprovalRequestStatus Status { get; private set; }

    /// <summary>Null once the request is terminal (Approved/Rejected/Cancelled).</summary>
    public int? CurrentStepNumber { get; private set; }

    public int StepCount { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public string? SubmittedIpAddress { get; private set; }

    public string? SubmittedUserAgent { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public IReadOnlyList<ApprovalTask> Tasks => _tasks.AsReadOnly();

    public IReadOnlyList<ApprovalHistoryEntry> History => _history.AsReadOnly();

    public ApprovalTask? CurrentTask => CurrentStepNumber is { } step ? _tasks.SingleOrDefault(t => t.StepNumber == step) : null;

    public static Result<ApprovalRequest> Submit(
        Guid tenantId, string requestType, Guid subjectId, Guid requesterEmployeeId, IReadOnlyList<ApprovalTaskSeed> steps,
        Guid? policyId, string? policyNameAtSubmission, Guid? resolvedFromOrgUnitId, bool resolvedByInheritance,
        bool showCurrentApproverToRequester, DateTime submittedAt, string? submittedIpAddress, string? submittedUserAgent, Guid? id = null)
    {
        if (requesterEmployeeId == Guid.Empty)
        {
            return Error.Validation(WorkflowDomainErrors.RequestRequesterRequired);
        }

        if (steps.Count == 0)
        {
            return Error.Validation(WorkflowDomainErrors.RequestNoSteps);
        }

        if (steps.Count > WorkflowLimits.MaxSteps)
        {
            return Error.Validation(WorkflowDomainErrors.PolicyMaxStepsExceeded, (nameof(WorkflowLimits.MaxSteps), WorkflowLimits.MaxSteps));
        }

        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].StepNumber != i + 1)
            {
                return Error.Validation(WorkflowDomainErrors.PolicyStepsNotContiguous);
            }

            if (steps[i].ApproverEmployeeId is null && string.IsNullOrWhiteSpace(steps[i].FallbackRoleCode))
            {
                return Error.Validation(WorkflowDomainErrors.PolicyStepApproverRequired);
            }
        }

        var request = new ApprovalRequest(
            id ?? Guid.CreateVersion7(), tenantId, requestType, subjectId, requesterEmployeeId,
            policyId, policyNameAtSubmission, resolvedFromOrgUnitId, resolvedByInheritance,
            showCurrentApproverToRequester, steps.Count, submittedAt, submittedIpAddress, submittedUserAgent);

        foreach (var step in steps)
        {
            request._tasks.Add(new ApprovalTask(request.Id, tenantId, step.StepNumber, step.ApproverEmployeeId, step.FallbackRoleCode, step.IsRequired));
        }

        request.AddHistory(ApprovalHistoryEventType.Submitted, 1, requesterEmployeeId, null, null, submittedIpAddress, submittedUserAgent, submittedAt, null);
        request.Raise(new ApprovalRequestSubmitted(request.Id, tenantId, requestType, subjectId, requesterEmployeeId));
        return request;
    }

    /// <summary>
    /// Records a decision on <paramref name="stepNumber"/>. Assignee/role/override authorization is the Application
    /// layer's job (it has access to caller identity, IRoleAssignmentLookup, and permission checks) - this method only
    /// enforces process invariants: the request is pending, the step is the current one, and its task is still pending.
    /// <paramref name="wasOverride"/> is a pure audit flag for "decided via workflow.request.override", not a bypass switch.
    /// </summary>
    public Result Decide(
        int stepNumber, Guid decidingEmployeeId, ApprovalOutcome outcome, string? comment, bool wasOverride,
        ApprovalChannel channel, string? ipAddress, string? userAgent, DateTime now)
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            return Error.Rule(WorkflowDomainErrors.RequestNotPending);
        }

        if (CurrentStepNumber != stepNumber)
        {
            return Error.Rule(WorkflowDomainErrors.TaskNotCurrent);
        }

        var task = _tasks.SingleOrDefault(t => t.StepNumber == stepNumber);
        if (task is null)
        {
            return Error.NotFound(WorkflowDomainErrors.TaskNotFound);
        }

        if (task.Status != ApprovalTaskStatus.Pending)
        {
            return Error.Rule(WorkflowDomainErrors.TaskNotPending);
        }

        task.Decide(decidingEmployeeId, outcome, comment, wasOverride, channel, ipAddress, userAgent, now);
        AddHistory(ApprovalHistoryEventType.Decided, stepNumber, decidingEmployeeId, outcome, channel, ipAddress, userAgent, now, comment);

        // A reject on a required step terminates the request; an optional step's reject is recorded but the request
        // still advances (assumption confirmed with the user - see plan §0 karar #2).
        if (outcome == ApprovalOutcome.Rejected && task.IsRequired)
        {
            Status = ApprovalRequestStatus.Rejected;
            CurrentStepNumber = null;
            CompletedAt = now;
            AddHistory(ApprovalHistoryEventType.Rejected, stepNumber, decidingEmployeeId, outcome, channel, ipAddress, userAgent, now, null);
            Raise(new ApprovalRequestRejected(Id, TenantId, RequestType, SubjectId, RequesterEmployeeId, stepNumber));
            return Result.Success();
        }

        if (stepNumber == StepCount)
        {
            Status = ApprovalRequestStatus.Approved;
            CurrentStepNumber = null;
            CompletedAt = now;
            AddHistory(ApprovalHistoryEventType.Completed, stepNumber, decidingEmployeeId, outcome, channel, ipAddress, userAgent, now, null);
            Raise(new ApprovalRequestApproved(Id, TenantId, RequestType, SubjectId, RequesterEmployeeId));
        }
        else
        {
            CurrentStepNumber = stepNumber + 1;
        }

        return Result.Success();
    }

    public Result Cancel(Guid actorEmployeeId, DateTime now)
    {
        if (Status != ApprovalRequestStatus.Pending)
        {
            return Error.Rule(WorkflowDomainErrors.RequestNotPending);
        }

        Status = ApprovalRequestStatus.Cancelled;
        CurrentStepNumber = null;
        CompletedAt = now;
        AddHistory(ApprovalHistoryEventType.Cancelled, null, actorEmployeeId, null, null, null, null, now, null);
        return Result.Success();
    }

    private void AddHistory(
        ApprovalHistoryEventType eventType, int? stepNumber, Guid? actorEmployeeId, ApprovalOutcome? outcome,
        ApprovalChannel? channel, string? ipAddress, string? userAgent, DateTime occurredAt, string? note) =>
        _history.Add(new ApprovalHistoryEntry(Id, TenantId, eventType, stepNumber, actorEmployeeId, outcome, channel, ipAddress, userAgent, occurredAt, note));
}

public sealed record ApprovalRequestSubmitted(Guid ApprovalRequestId, Guid TenantId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId) : DomainEvent;

public sealed record ApprovalRequestApproved(Guid ApprovalRequestId, Guid TenantId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId) : DomainEvent;

public sealed record ApprovalRequestRejected(Guid ApprovalRequestId, Guid TenantId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId, int RejectedAtStep) : DomainEvent;
