using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Domain.ApprovalRequests;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Workflow.Application.ApprovalRequests;

/// <summary>
/// Approval request status for a caller-identity-gated read (see GetApprovalStatusQuery). CurrentApproverEmployeeId is
/// only populated when the request's ShowCurrentApproverToRequester flag is set and the current task has a concrete assignee.
/// FG-009 appended SubmittedAt/CompletedAt and the per-step timeline (<see cref="Steps"/>, ordered by step number).
/// </summary>
public sealed record ApprovalRequestStatusDto(
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    string Status,
    int StepCount,
    int? CurrentStepNumber,
    Guid? CurrentApproverEmployeeId,
    DateTime SubmittedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ApprovalStepStatusDto> Steps);

/// <summary>
/// FG-009: one step of the request's approval chain. A decided step names who actually decided it (the assignee, a
/// fallback role member or an override holder) with the decision time and comment; a pending step names its assigned
/// approver - except to the requester when the policy hides the approver (ShowCurrentApproverToRequester = false), and
/// never for a role-queue step (<see cref="IsFallbackRole"/>). A pending step of a request that is no longer pending
/// was never reached. ApproverName is best-effort (null when the directory does not resolve the employee).
/// </summary>
public sealed record ApprovalStepStatusDto(
    int StepNumber,
    ApprovalTaskStatus Status,
    Guid? ApproverEmployeeId,
    string? ApproverName,
    bool IsFallbackRole,
    DateTime? DecidedAt,
    string? Comment);

/// <summary>FG-009: one comment on an approval request, oldest first. AuthorName is best-effort like <see cref="ApprovalStepStatusDto.ApproverName"/>.</summary>
public sealed record ApprovalRequestCommentDto(Guid Id, Guid AuthorEmployeeId, string? AuthorName, string Text, DateTime CreatedAt);

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

/// <summary>
/// FG-008: one task the caller decided, for the approval history list (newest decision first). Outcome is the caller's
/// own decision on this task; RequestStatus is the parent request's current state (a step-1 approval can still end
/// in a later step's rejection). RequesterName/Summary are best-effort like <see cref="InboxTaskDto"/>.
/// </summary>
public sealed record ApprovalHistoryItemDto(
    Guid TaskId,
    Guid ApprovalRequestId,
    string RequestType,
    Guid SubjectId,
    Guid RequesterEmployeeId,
    string? RequesterName,
    int StepNumber,
    ApprovalOutcome Outcome,
    DateTime DecidedAt,
    string? Comment,
    ApprovalRequestStatus RequestStatus,
    ApprovalRequestSummary? Summary);

/// <summary>FG-008 decide-bulk: one task's outcome - <see cref="Error"/> is null when the task was decided.</summary>
public sealed record BulkDecisionTaskResult(Guid TaskId, Error? Error)
{
    public bool Succeeded => Error is null;
}
