using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Domain.ApprovalRequests;

/// <summary>Single-use, expiring token for the "decide by email" flow - the raw token is never persisted, only its hash (see IEmailTokenGenerator).</summary>
public sealed class EmailDecisionToken : TenantEntity<Guid>
{
    private EmailDecisionToken()
    {
    }

    public EmailDecisionToken(Guid approvalTaskId, Guid tenantId, string tokenHash, DateTime expiresAt)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ApprovalTaskId = approvalTaskId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid ApprovalTaskId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTime ExpiresAt { get; private set; }

    public DateTime? UsedAt { get; private set; }

    public Result MarkUsed(DateTime now)
    {
        if (UsedAt is not null)
        {
            return Error.Rule(WorkflowDomainErrors.TokenAlreadyUsed);
        }

        if (now > ExpiresAt)
        {
            return Error.Rule(WorkflowDomainErrors.TokenExpired);
        }

        UsedAt = now;
        return Result.Success();
    }
}
