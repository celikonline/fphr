using Sense.Modules.Workflow.Contracts;

namespace Sense.Modules.Workflow.Application.ApprovalRequests;

/// <summary>
/// Approval request status for a caller-identity-gated read (see GetApprovalStatusQuery). CurrentApproverEmployeeId is
/// only populated when the request's ShowCurrentApproverToRequester flag is set and the current task has a concrete assignee.
/// </summary>
public sealed record ApprovalRequestStatusDto(
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    string Status,
    int StepCount,
    int? CurrentStepNumber,
    Guid? CurrentApproverEmployeeId);

/// <summary>
/// One pending task in the caller's approval inbox (assigned directly, or via a fallback role queue). Summary is the
/// request-type-specific projection from IApprovalRequestSummaryProvider (null when RequestType has no registered
/// provider, or the subject entity no longer resolves) - the caller shows RequestType's label alone in that case.
/// </summary>
public sealed record InboxTaskDto(
    Guid TaskId,
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    string? RequesterName,
    int StepNumber,
    bool IsFallbackRoleTask,
    DateTime SubmittedAt,
    ApprovalRequestSummary? Summary);

/// <summary>Summary row for the caller's own submitted approval requests.</summary>
public sealed record MyApprovalRequestDto(
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    string Status,
    int StepCount,
    int? CurrentStepNumber,
    DateTime SubmittedAt,
    DateTime? CompletedAt);
