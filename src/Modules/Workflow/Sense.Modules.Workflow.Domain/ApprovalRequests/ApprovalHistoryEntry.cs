using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Workflow.Domain.ApprovalRequests;

/// <summary>Append-only audit trail entry - never updated or deleted once written (court-evidence requirement, 11.3.04).</summary>
public sealed class ApprovalHistoryEntry : TenantEntity<Guid>
{
    private ApprovalHistoryEntry()
    {
    }

    internal ApprovalHistoryEntry(
        Guid approvalRequestId, Guid tenantId, ApprovalHistoryEventType eventType, int? stepNumber, Guid? actorEmployeeId,
        ApprovalOutcome? outcome, ApprovalChannel? channel, string? ipAddress, string? userAgent, DateTime occurredAt, string? note)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ApprovalRequestId = approvalRequestId;
        EventType = eventType;
        StepNumber = stepNumber;
        ActorEmployeeId = actorEmployeeId;
        Outcome = outcome;
        Channel = channel;
        IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : Guard.MaxLength(ipAddress, WorkflowLimits.IpAddressMaxLength);
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : Guard.MaxLength(userAgent, WorkflowLimits.UserAgentMaxLength);
        OccurredAt = occurredAt;
        Note = string.IsNullOrWhiteSpace(note) ? null : Guard.MaxLength(note.Trim(), WorkflowLimits.CommentMaxLength);
    }

    public Guid ApprovalRequestId { get; private set; }

    public ApprovalHistoryEventType EventType { get; private set; }

    public int? StepNumber { get; private set; }

    public Guid? ActorEmployeeId { get; private set; }

    public ApprovalOutcome? Outcome { get; private set; }

    public ApprovalChannel? Channel { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public string? Note { get; private set; }
}
