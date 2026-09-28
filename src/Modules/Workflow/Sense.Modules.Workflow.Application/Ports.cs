using Sense.Modules.Workflow.Application.ApprovalRequests;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application;

/// <summary>Token generation/hashing for the "decide by email" flow (SHA-256, mirrors Identity's ISecretGenerator).</summary>
public interface IEmailTokenGenerator
{
    string NewToken(int bytes = 32);

    string Hash(string token);
}

/// <summary>Frontend links for email-driven decisions.</summary>
public interface IWorkflowFrontendLinks
{
    string DecisionLink(string rawToken);
}

/// <summary>Result of resolving who should decide a newly-submitted request, and where that resolution came from (for the request's snapshot fields).</summary>
public sealed record ApproverResolution(
    IReadOnlyList<ApprovalTaskSeed> Steps, Guid? PolicyId, string? PolicyName, Guid? ResolvedFromOrgUnitId, bool ResolvedByInheritance);

/// <summary>
/// Default approver chain resolution when a request is submitted without an explicit step list. Order: org-unit
/// approval policy assignment (Organization.Contracts.IOrgApprovalPolicyResolver) -&gt; direct manager
/// (Employee.Contracts.IEmployeeLookup.GetManagerEmployeeIdAsync) -&gt; fallback role queue
/// (Identity.Contracts.IRoleAssignmentLookup, TenantSettings.Workflow.FallbackApproverRoleCode ?? SystemRoleCodes.HR).
/// Implemented by Sense.Modules.Workflow.Infrastructure.
/// </summary>
public interface IApproverResolver
{
    Task<Result<ApproverResolution>> ResolveAsync(Guid tenantId, string requestType, Guid requesterEmployeeId, CancellationToken ct = default);
}

/// <summary>
/// FG-008 decide-bulk: runs one <see cref="DecideApprovalTaskCommand"/> through the full command pipeline in its own DI
/// scope (fresh DbContext, own UnitOfWork transaction) on behalf of the current caller, so one task's failure - a
/// business rule or an unexpected exception - can neither roll back nor leak tracked changes into another task.
/// Implemented by Sense.Modules.Workflow.Infrastructure.
/// </summary>
public interface IIsolatedApprovalDecisionRunner
{
    Task<Result> DecideAsync(DecideApprovalTaskCommand command, CancellationToken ct = default);
}
