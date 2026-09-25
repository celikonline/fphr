using Microsoft.EntityFrameworkCore;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Domain.Delegations;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Infrastructure.Persistence;

/// <summary>EF Core-backed ApprovalPolicy repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class ApprovalPolicyRepository(WorkflowDbContext context) : IApprovalPolicyRepository
{
    public void Add(ApprovalPolicy policy) => context.ApprovalPolicies.Add(policy);

    public async Task<Result<ApprovalPolicy>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var policy = await context.ApprovalPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return policy is null ? Error.NotFound(WorkflowDomainErrors.PolicyNotFound) : policy;
    }

    public async Task<ApprovalPolicy?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.ApprovalPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApprovalPolicy>> ListAsync(string? requestType = null, CancellationToken cancellationToken = default)
    {
        var query = context.ApprovalPolicies.AsQueryable();
        if (!string.IsNullOrWhiteSpace(requestType))
        {
            query = query.Where(p => p.RequestType == requestType);
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed ApprovalRequest repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class ApprovalRequestRepository(WorkflowDbContext context) : IApprovalRequestRepository
{
    public void Add(ApprovalRequest request) => context.ApprovalRequests.Add(request);

    public async Task<Result<ApprovalRequest>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var request = await context.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return request is null ? Error.NotFound(WorkflowDomainErrors.RequestNotFound) : request;
    }

    public async Task<IReadOnlyList<ApprovalTask>> ListInboxTasksAsync(
        Guid employeeId, IReadOnlyCollection<string> fallbackRoleCodes, IReadOnlyCollection<Guid> delegatorEmployeeIds,
        CancellationToken cancellationToken = default)
    {
        var roleCodes = fallbackRoleCodes.ToList();
        var delegatorIds = delegatorEmployeeIds.ToList();
        // A task stays Pending after its request is cancelled (withdrawn/edited) or rejected at an earlier step, so the
        // request must still be Pending - otherwise the approver sees dead items that fail with RequestNotPending.
        return await context.ApprovalTasks
            .Where(t => t.Status == ApprovalTaskStatus.Pending
                && context.ApprovalRequests.Any(r => r.Id == t.ApprovalRequestId && r.Status == ApprovalRequestStatus.Pending)
                && (t.ApproverEmployeeId == employeeId
                    || (t.ApproverEmployeeId != null && delegatorIds.Contains(t.ApproverEmployeeId.Value))
                    || (t.FallbackRoleCode != null && roleCodes.Contains(t.FallbackRoleCode))))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListByRequesterAsync(Guid requesterEmployeeId, CancellationToken cancellationToken = default) =>
        await context.ApprovalRequests
            .Where(r => r.RequesterEmployeeId == requesterEmployeeId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<Result<ApprovalTask>> GetTaskByIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await context.ApprovalTasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        return task is null ? Error.NotFound(WorkflowDomainErrors.TaskNotFound) : task;
    }

    /// <summary>Loads the owning ApprovalRequest (tracked, with Tasks/History via AutoInclude) - decisions are made through the aggregate root.</summary>
    public async Task<Result<ApprovalRequest>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var requestId = await context.ApprovalTasks
            .Where(t => t.Id == taskId)
            .Select(t => (Guid?)t.ApprovalRequestId)
            .FirstOrDefaultAsync(cancellationToken);

        if (requestId is null)
        {
            return Error.NotFound(WorkflowDomainErrors.TaskNotFound);
        }

        var request = await context.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        return request is null ? Error.NotFound(WorkflowDomainErrors.RequestNotFound) : request;
    }
}

/// <summary>EF Core-backed EmailDecisionToken repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class EmailDecisionTokenRepository(WorkflowDbContext context) : IEmailDecisionTokenRepository
{
    public void Add(EmailDecisionToken token) => context.EmailDecisionTokens.Add(token);

    public async Task<Result<EmailDecisionToken>> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        var token = await context.EmailDecisionTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
        return token is null ? Error.NotFound(WorkflowDomainErrors.TokenNotFound) : token;
    }
}

/// <summary>EF Core-backed ApprovalDelegation repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class ApprovalDelegationRepository(WorkflowDbContext context) : IApprovalDelegationRepository
{
    public void Add(ApprovalDelegation delegation) => context.ApprovalDelegations.Add(delegation);

    public async Task<Result<ApprovalDelegation>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var found = await context.ApprovalDelegations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return found is null ? Error.NotFound(WorkflowDomainErrors.DelegationNotFound) : found;
    }

    public async Task<IReadOnlyList<ApprovalDelegation>> ListActiveForDelegateAsync(Guid delegateEmployeeId, DateOnly today, CancellationToken cancellationToken = default) =>
        await context.ApprovalDelegations
            .Where(d => d.DelegateEmployeeId == delegateEmployeeId && d.RevokedAt == null && d.StartDate <= today && d.EndDate >= today)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovalDelegation>> ListInvolvingAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.ApprovalDelegations
            .Where(d => d.DelegatorEmployeeId == employeeId || d.DelegateEmployeeId == employeeId)
            .OrderByDescending(d => d.StartDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApprovalDelegation>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await context.ApprovalDelegations.OrderByDescending(d => d.StartDate).ToListAsync(cancellationToken);
}
