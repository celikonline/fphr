using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Workflow.Contracts.Events;

/// <summary>Published (via Workflow's outbox) when every required step of an approval request has approved.</summary>
public sealed record ApprovalCompleted(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId)
    : IntegrationEvent(TenantId);

/// <summary>Published (via Workflow's outbox) when a required step rejects an approval request.</summary>
public sealed record ApprovalRejected(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId, int RejectedAtStep)
    : IntegrationEvent(TenantId);

/// <summary>
/// Published (via Workflow's outbox) when an approval request is submitted - the "talep oluşturuldu" notification to the
/// requester. Not named ApprovalRequestSubmitted: that is the Workflow domain event's name, and EventTypeRegistry keys
/// types by "Module.TypeName".
/// </summary>
public sealed record ApprovalRequested(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId)
    : IntegrationEvent(TenantId);

/// <summary>
/// Published (via Workflow's outbox) whenever a task becomes the request's current step (on submit, and after each
/// decision that advances the request). Exactly one of <paramref name="ApproverEmployeeId"/> /
/// <paramref name="FallbackRoleCode"/> is set. The e-mail decision link is not carried here - consumers obtain it at
/// send time via <see cref="IApprovalDecisionLinkIssuer"/> so the raw token never sits in an outbox row.
/// </summary>
public sealed record ApprovalTaskAssigned(
    Guid TenantId,
    Guid ApprovalRequestId,
    Guid ApprovalTaskId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    int StepNumber,
    Guid? ApproverEmployeeId,
    string? FallbackRoleCode)
    : IntegrationEvent(TenantId);

/// <summary>Published (via Workflow's outbox) when an approval delegation is created - the delegate is notified (Z2.2).</summary>
public sealed record ApprovalDelegationAssigned(
    Guid TenantId, Guid DelegationId, Guid DelegatorEmployeeId, Guid DelegateEmployeeId, DateOnly StartDate, DateOnly EndDate)
    : IntegrationEvent(TenantId);

/// <summary>
/// FG-009: published (via Workflow's outbox) when a party of an approval request leaves a comment on it.
/// <paramref name="RecipientEmployeeIds"/> is already resolved by Workflow - the requester plus every concrete approver
/// and decider of the request's tasks (and the fallback role members of a pending role-queue step), author excluded -
/// so consumers only notify, they do not re-derive who is a party. The comment text itself is not carried.
/// </summary>
public sealed record ApprovalRequestCommented(
    Guid TenantId,
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    Guid AuthorEmployeeId,
    IReadOnlyList<Guid> RecipientEmployeeIds)
    : IntegrationEvent(TenantId);
