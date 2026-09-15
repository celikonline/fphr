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
