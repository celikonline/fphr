namespace Sense.Modules.Workflow.Application;

/// <summary>Application layer error codes (localized in the Web layer).</summary>
public static class WorkflowErrors
{
    public const string PolicyNotFound = "workflow.policy.not_found";
    public const string PolicyMaxStepsExceeded = "workflow.policy.max_steps_exceeded";
    public const string PolicyStepApproverInvalid = "workflow.policy.step_approver_invalid";

    public const string RequestNotFound = "workflow.request.not_found";
    public const string RequestNotPending = "workflow.request.not_pending";
    public const string RequestApproversAlreadyEdited = "workflow.request.approvers_already_edited";
    public const string EditApproversNotAuthorized = "workflow.request.edit_approvers_not_authorized";

    public const string TaskNotFound = "workflow.task.not_found";
    public const string TaskNotCurrent = "workflow.task.not_current";
    public const string AssigneeMismatch = "workflow.task.assignee_mismatch";
    public const string SelfApprovalNotAllowed = "workflow.task.self_approval_not_allowed";

    public const string TokenInvalid = "workflow.token.invalid";
    public const string TokenExpired = "workflow.token.expired";
    public const string TokenAlreadyUsed = "workflow.token.already_used";

    public const string RequesterNotFound = "workflow.request.requester_not_found";

    public const string BulkEmpty = "workflow.bulk.empty";
    public const string BulkTooManyTasks = "workflow.bulk.too_many_tasks";
}
