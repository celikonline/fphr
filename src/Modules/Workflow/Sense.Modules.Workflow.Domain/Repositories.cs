using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Domain;

/// <summary>ApprovalPolicy aggregate repository.</summary>
public interface IApprovalPolicyRepository
{
    void Add(ApprovalPolicy policy);

    Task<Result<ApprovalPolicy>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Null-safe variant for resolvers that must tolerate a stale/deleted policy id (see Organization S1 note).</summary>
    Task<ApprovalPolicy?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalPolicy>> ListAsync(string? requestType = null, CancellationToken cancellationToken = default);
}

/// <summary>ApprovalRequest aggregate repository.</summary>
public interface IApprovalRequestRepository
{
    void Add(ApprovalRequest request);

    Task<Result<ApprovalRequest>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Pending tasks assigned directly to the employee, or via a matching fallback role queue.</summary>
    Task<IReadOnlyList<ApprovalTask>> ListInboxTasksAsync(Guid employeeId, IReadOnlyCollection<string> fallbackRoleCodes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalRequest>> ListByRequesterAsync(Guid requesterEmployeeId, CancellationToken cancellationToken = default);

    Task<Result<ApprovalTask>> GetTaskByIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Loads the owning ApprovalRequest (tracked) for a task id - decisions are made through the aggregate root.</summary>
    Task<Result<ApprovalRequest>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);
}

/// <summary>EmailDecisionToken repository.</summary>
public interface IEmailDecisionTokenRepository
{
    void Add(EmailDecisionToken token);

    Task<Result<EmailDecisionToken>> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
}
