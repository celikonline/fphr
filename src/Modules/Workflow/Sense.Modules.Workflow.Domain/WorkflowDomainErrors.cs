namespace Sense.Modules.Workflow.Domain;

/// <summary>Domain error codes for Workflow module.</summary>
public static class WorkflowDomainErrors
{
    public const string PolicyNotFound = "workflow.policy.not_found";
    public const string PolicyNameRequired = "workflow.policy.name_required";
    public const string PolicyRequestTypeRequired = "workflow.policy.request_type_required";
    public const string PolicyRequestTypeInvalid = "workflow.policy.request_type_invalid";
    public const string PolicyStepsRequired = "workflow.policy.steps_required";
    public const string PolicyMaxStepsExceeded = "workflow.policy.max_steps_exceeded";
    public const string PolicyStepsNotContiguous = "workflow.policy.steps_not_contiguous";
    public const string PolicyStepApproverRequired = "workflow.policy.step_approver_required";
    public const string PolicyAlreadyInactive = "workflow.policy.already_inactive";

    public const string RequestNotFound = "workflow.request.not_found";
    public const string RequestNotPending = "workflow.request.not_pending";
    public const string RequestNoSteps = "workflow.request.no_steps";
    public const string RequestRequesterRequired = "workflow.request.requester_required";

    /// <summary>TK-009: the approver list on a request may be edited once - a second edit attempt hits this.</summary>
    public const string RequestApproversAlreadyEdited = "workflow.request.approvers_already_edited";

    public const string TaskNotFound = "workflow.task.not_found";
    public const string TaskNotCurrent = "workflow.task.not_current";
    public const string TaskNotPending = "workflow.task.not_pending";
    public const string TaskAssigneeMismatch = "workflow.task.assignee_mismatch";

    /// <summary>Ürün kararı (denetim A10): bir görevi reddederken yorum (ret gerekçesi) zorunlu; onayda isteğe bağlı.</summary>
    public const string RejectionCommentRequired = "workflow.task.rejection_comment_required";

    /// <summary>FG-009: a cancelled request takes no more comments.</summary>
    public const string CommentRequestClosed = "workflow.comment.request_closed";

    /// <summary>FG-009: an empty/whitespace-only comment.</summary>
    public const string CommentTextRequired = "workflow.comment.text_required";

    public const string TokenNotFound = "workflow.token.not_found";
    public const string TokenExpired = "workflow.token.expired";
    public const string TokenAlreadyUsed = "workflow.token.already_used";
}

/// <summary>Domain constants and length limits.</summary>
public static class WorkflowLimits
{
    public const int NameMaxLength = 200;
    public const int RequestTypeMaxLength = 50;
    public const int MaxSteps = 6;
    public const int CommentMaxLength = 1000;
    public const int IpAddressMaxLength = 64;
    public const int UserAgentMaxLength = 500;
    public const int RoleCodeMaxLength = 50;

    /// <summary>FG-008: most tasks one decide-bulk call may carry (each is decided in its own transaction).</summary>
    public const int BulkDecisionMaxTasks = 50;

    /// <summary>How long a "decide by e-mail" link stays valid after it is minted.</summary>
    public static readonly TimeSpan EmailDecisionTokenLifetime = TimeSpan.FromDays(7);
}
