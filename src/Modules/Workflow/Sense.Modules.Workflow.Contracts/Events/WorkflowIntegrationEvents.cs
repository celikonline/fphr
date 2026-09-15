using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Workflow.Contracts.Events;

/// <summary>Published (via Workflow's outbox) when every required step of an approval request has approved.</summary>
public sealed record ApprovalCompleted(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId)
    : IntegrationEvent(TenantId);

/// <summary>Published (via Workflow's outbox) when a required step rejects an approval request.</summary>
public sealed record ApprovalRejected(Guid TenantId, Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId, int RejectedAtStep)
    : IntegrationEvent(TenantId);
