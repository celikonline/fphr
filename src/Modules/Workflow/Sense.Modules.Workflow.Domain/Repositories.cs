using Sense.Modules.Workflow.Domain.ApprovalPolicies;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Modules.Workflow.Domain.Delegations;
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

    /// <summary>
    /// Pending tasks assigned directly to the employee, via a matching fallback role queue, or assigned to one of
    /// <paramref name="delegatorEmployeeIds"/> (active delegators, Z2.2 - request-type scoping is applied by the caller).
    /// </summary>
    Task<IReadOnlyList<ApprovalTask>> ListInboxTasksAsync(
        Guid employeeId, IReadOnlyCollection<string> fallbackRoleCodes, IReadOnlyCollection<Guid> delegatorEmployeeIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalRequest>> ListByRequesterAsync(Guid requesterEmployeeId, CancellationToken cancellationToken = default);

    Task<Result<ApprovalTask>> GetTaskByIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Loads the owning ApprovalRequest (tracked) for a task id - decisions are made through the aggregate root.</summary>
    Task<Result<ApprovalRequest>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// FG-008: one page of the tasks the employee decided (DecidedByEmployeeId), newest decision first. Optional
    /// filters: decided in [<paramref name="decidedFromUtc"/>, <paramref name="decidedToUtcExclusive"/>) and the
    /// parent request's type.
    /// </summary>
    Task<DecidedTaskPage> ListDecidedTasksAsync(
        Guid employeeId, DateTime? decidedFromUtc, DateTime? decidedToUtcExclusive, string? requestType, int skip, int take,
        CancellationToken cancellationToken = default);
}

/// <summary>A decided task joined with the parent request fields the history list shows (read model, not tracked).</summary>
public sealed record DecidedTaskRow(
    Guid TaskId,
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    int StepNumber,
    ApprovalTaskStatus TaskStatus,
    DateTime DecidedAt,
    string? Comment,
    ApprovalRequestStatus RequestStatus);

/// <summary>One page of <see cref="DecidedTaskRow"/> plus the unpaged total.</summary>
public sealed record DecidedTaskPage(IReadOnlyList<DecidedTaskRow> Items, long TotalCount);

/// <summary>EmailDecisionToken repository.</summary>
public interface IEmailDecisionTokenRepository
{
    void Add(EmailDecisionToken token);

    Task<Result<EmailDecisionToken>> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
}

/// <summary>ApprovalDelegation repository (Z2.2 vekalet).</summary>
public interface IApprovalDelegationRepository
{
    void Add(ApprovalDelegation delegation);

    Task<Result<ApprovalDelegation>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Non-revoked delegations to the delegate whose date range covers <paramref name="today"/> (request-type scope not applied).</summary>
    Task<IReadOnlyList<ApprovalDelegation>> ListActiveForDelegateAsync(Guid delegateEmployeeId, DateOnly today, CancellationToken cancellationToken = default);

    /// <summary>All delegations where the employee is the delegator or the delegate, newest first.</summary>
    Task<IReadOnlyList<ApprovalDelegation>> ListInvolvingAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalDelegation>> ListAllAsync(CancellationToken cancellationToken = default);
}
