namespace Sense.Modules.Workflow.Contracts;

/// <summary>A freshly minted single-use "decide by e-mail" link and when it stops working.</summary>
public sealed record ApprovalDecisionLink(string Url, DateTime ExpiresAtUtc);

/// <summary>
/// Mints the single-use decision link for an approval task at the moment an e-mail is actually sent (Notifications'
/// delivery processor), so the raw token only ever exists in memory and in the sent e-mail. Persists the token hash
/// immediately (own SaveChanges - callers are outside Workflow's unit of work).
/// </summary>
public interface IApprovalDecisionLinkIssuer
{
    /// <summary>
    /// Null when the task is no longer actionable by e-mail: not found, no longer the request's current step, the
    /// request is not pending, or it has no concrete approver (fallback role queue).
    /// </summary>
    Task<ApprovalDecisionLink?> IssueAsync(Guid tenantId, Guid approvalTaskId, CancellationToken ct = default);
}
