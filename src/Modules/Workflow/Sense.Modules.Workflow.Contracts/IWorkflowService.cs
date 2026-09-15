using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Contracts;

/// <summary>One resolved or explicitly-supplied approver step for a request being submitted.</summary>
public sealed record ApprovalStepInput(int StepNumber, Guid ApproverEmployeeId, bool IsRequired);

/// <summary>
/// Cross-module write port other modules (future Leave, Expense, ...) use to start/cancel an approval process.
/// Implemented by Sense.Modules.Workflow.Infrastructure, registered by WorkflowModule.AddModule. The caller is
/// responsible for its own permission check (e.g. "leave.requests.create") before calling StartAsync - Workflow
/// only enforces process integrity, not who may start a process on a subject's behalf.
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Starts an approval process for <paramref name="requestType"/>/<paramref name="subjectId"/>.
    /// <paramref name="explicitSteps"/> null = resolve via org-unit assignment / policy / direct manager / role
    /// queue fallback (see Workflow.Infrastructure.IApproverResolver); non-null = use exactly these steps
    /// ("talep anında onaycı listesi düzenleme" - the caller already resolved/edited the chain).
    /// </summary>
    Task<Result<Guid>> StartAsync(
        string requestType, Guid subjectId, Guid requesterEmployeeId, IReadOnlyList<ApprovalStepInput>? explicitSteps,
        string? submittedIpAddress, string? submittedUserAgent, CancellationToken ct = default);

    Task<Result> CancelAsync(Guid approvalRequestId, Guid actorEmployeeId, CancellationToken ct = default);
}
