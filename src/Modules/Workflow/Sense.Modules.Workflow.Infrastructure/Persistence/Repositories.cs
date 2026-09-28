using Microsoft.EntityFrameworkCore;
using Sense.Modules.Workflow.Domain;
using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
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
        Guid employeeId, IReadOnlyCollection<string> fallbackRoleCodes, CancellationToken cancellationToken = default)
    {
        var roleCodes = fallbackRoleCodes.ToList();
        // A task stays Pending after its request is cancelled (withdrawn/edited) or rejected at an earlier step, so the
        // request must still be Pending - otherwise the approver sees dead items that fail with RequestNotPending.
        return await context.ApprovalTasks
            .Where(t => t.Status == ApprovalTaskStatus.Pending
                && context.ApprovalRequests.Any(r => r.Id == t.ApprovalRequestId && r.Status == ApprovalRequestStatus.Pending)
                && (t.ApproverEmployeeId == employeeId || (t.FallbackRoleCode != null && roleCodes.Contains(t.FallbackRoleCode))))
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

    public async Task<DecidedTaskPage> ListDecidedTasksAsync(
        Guid employeeId, DateTime? decidedFromUtc, DateTime? decidedToUtcExclusive, string? requestType, int skip, int take,
        CancellationToken cancellationToken = default)
    {
        var query =
            from t in context.ApprovalTasks.AsNoTracking()
            join r in context.ApprovalRequests.AsNoTracking() on t.ApprovalRequestId equals r.Id
            where t.DecidedByEmployeeId == employeeId && t.Status != ApprovalTaskStatus.Pending && t.DecidedAt != null
            select new { Task = t, Request = r };

        if (decidedFromUtc is { } from)
        {
            query = query.Where(x => x.Task.DecidedAt >= from);
        }

        if (decidedToUtcExclusive is { } to)
        {
            query = query.Where(x => x.Task.DecidedAt < to);
        }

        if (!string.IsNullOrWhiteSpace(requestType))
        {
            query = query.Where(x => x.Request.RequestType == requestType);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.Task.DecidedAt)
            .ThenByDescending(x => x.Task.Id)
            .Skip(skip)
            .Take(take)
            .Select(x => new DecidedTaskRow(
                x.Task.Id, x.Request.Id, x.Request.RequestType, x.Request.SubjectId, x.Request.RequesterEmployeeId,
                x.Task.StepNumber, x.Task.Status, x.Task.DecidedAt!.Value, x.Task.Comment, x.Request.Status))
            .ToListAsync(cancellationToken);

        return new DecidedTaskPage(items, total);
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
