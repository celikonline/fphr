namespace Sense.Modules.Workflow.Contracts;

/// <summary>One step of a resolved approval request's chain, for cross-module/UI status display.</summary>
public sealed record ApprovalStepStatus(
    int StepNumber, Guid? ApproverEmployeeId, string? FallbackRoleCode, bool IsRequired, string Status, string? Comment = null);

/// <summary>Effective status of an approval request, for cross-module/UI reads that don't need the full Application query pipeline.</summary>
public sealed record ApprovalStatusDto(
    Guid ApprovalRequestId, string RequestType, Guid SubjectId, Guid RequesterEmployeeId, string Status,
    int? CurrentStepNumber, bool ShowCurrentApproverToRequester, IReadOnlyList<ApprovalStepStatus> Steps);

/// <summary>
/// Cross-module read port for approval request status (mirrors Organization.Contracts.IOrgUnitLookup's shape).
/// Implemented by Sense.Modules.Workflow.Infrastructure, registered by WorkflowModule.AddModule.
/// </summary>
public interface IApprovalRequestLookup
{
    Task<ApprovalStatusDto?> GetStatusAsync(Guid tenantId, Guid approvalRequestId, CancellationToken ct = default);
}
