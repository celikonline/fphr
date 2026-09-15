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

/// <summary>
/// Sends the "you have a pending approval" email carrying the single-use decision link. B1 uses a minimal inline
/// TR-only body via Sense.Shared.Contracts.Notifications.IEmailSender - real per-tenant/localized templating is
/// B2 Notifications' job (see plan §9 kapsam dışı).
/// </summary>
public interface IApprovalMailer
{
    Task SendDecisionRequestAsync(
        string toEmail, string toName, string requestType, string decisionUrl, DateTime expiresAtUtc, CancellationToken ct);
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
