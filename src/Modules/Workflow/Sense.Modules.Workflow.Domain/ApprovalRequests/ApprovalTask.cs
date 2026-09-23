using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Workflow.Domain.ApprovalRequests;

/// <summary>A single rung of a submitted request's chain - a snapshot, never re-reads the source policy.</summary>
public sealed class ApprovalTask : TenantEntity<Guid>, IAuditLogged
{
    private ApprovalTask()
    {
    }

    internal ApprovalTask(Guid approvalRequestId, Guid tenantId, int stepNumber, Guid? approverEmployeeId, string? fallbackRoleCode, bool isRequired)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ApprovalRequestId = approvalRequestId;
        StepNumber = stepNumber;
        ApproverEmployeeId = approverEmployeeId;
        FallbackRoleCode = fallbackRoleCode;
        IsRequired = isRequired;
        Status = ApprovalTaskStatus.Pending;
    }

    public Guid ApprovalRequestId { get; private set; }

    public int StepNumber { get; private set; }

    /// <summary>Null only when no policy/manager resolved and the task fell back to a role queue (see <see cref="FallbackRoleCode"/>).</summary>
    public Guid? ApproverEmployeeId { get; private set; }

    /// <summary>System role code (e.g. "HR") whose members may decide this task when <see cref="ApproverEmployeeId"/> is null.</summary>
    public string? FallbackRoleCode { get; private set; }

    public bool IsRequired { get; private set; }

    public ApprovalTaskStatus Status { get; private set; }

    public Guid? DecidedByEmployeeId { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public string? Comment { get; private set; }

    public bool WasOverride { get; private set; }

    public ApprovalChannel? DecisionChannel { get; private set; }

    public string? DecisionIpAddress { get; private set; }

    public string? DecisionUserAgent { get; private set; }

    /// <summary>Reassigns this still-pending task to a different specific employee (TK-009 "talep anında onaycı listesi düzenleme") - the caller (ApprovalRequest.EditApprovers) already checked Status is Pending.</summary>
    internal void ReassignApprover(Guid approverEmployeeId)
    {
        ApproverEmployeeId = approverEmployeeId;
        FallbackRoleCode = null;
    }

    internal void Decide(Guid decidingEmployeeId, ApprovalOutcome outcome, string? comment, bool wasOverride, ApprovalChannel channel, string? ipAddress, string? userAgent, DateTime now)
    {
        Status = outcome == ApprovalOutcome.Approved ? ApprovalTaskStatus.Approved : ApprovalTaskStatus.Rejected;
        DecidedByEmployeeId = decidingEmployeeId;
        DecidedAt = now;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : Guard.MaxLength(comment.Trim(), WorkflowLimits.CommentMaxLength);
        WasOverride = wasOverride;
        DecisionChannel = channel;
        DecisionIpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : Guard.MaxLength(ipAddress, WorkflowLimits.IpAddressMaxLength);
        DecisionUserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : Guard.MaxLength(userAgent, WorkflowLimits.UserAgentMaxLength);
    }
}
