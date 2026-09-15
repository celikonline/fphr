using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Workflow.Contracts.Events;

/// <summary>Published (via Workflow's outbox) when every required step of an approval request has approved.</summary>
public sealed record ApprovalCompleted(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId)
    : IntegrationEvent(TenantId);

/// <summary>Published (via Workflow's outbox) when a required step rejects an approval request.</summary>
public sealed record ApprovalRejected(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId, int RejectedAtStep)
    : IntegrationEvent(TenantId);

/// <summary>Published (via Workflow's outbox) when an approval request is submitted - "talep oluşturuldu" notification (#43).</summary>
public sealed record ApprovalRequestCreated(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId)
    : IntegrationEvent(TenantId);

/// <summary>
/// Published (via Workflow's outbox) whenever a task becomes the current step (on submit and after each approving decision).
/// ApproverEmployeeId null = fallback role queue (FallbackRoleCode). Workflow e-mails a concrete assignee itself (ApprovalMailer),
/// so consumers should not e-mail the approver again.
/// </summary>
public sealed record ApprovalTaskAssigned(
    Guid TenantId, Guid ApprovalRequestId, Guid ApprovalTaskId, string RequestType, Guid RequesterEmployeeId, int StepNumber,
    Guid? ApproverEmployeeId, string? FallbackRoleCode)
    : IntegrationEvent(TenantId);
