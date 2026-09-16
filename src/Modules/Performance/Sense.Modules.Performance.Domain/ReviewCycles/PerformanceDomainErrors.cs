namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>Domain error codes for Performance module.</summary>
public static class PerformanceDomainErrors
{
    public const string CycleNotFound = "performance.cycle.not_found";
    public const string CycleNameRequired = "performance.cycle.name_required";
    public const string CyclePeriodInvalid = "performance.cycle.period_invalid";
    public const string CycleNotDraft = "performance.cycle.not_draft";
    public const string CycleNotPublished = "performance.cycle.not_published";

    public const string CompetencyRequired = "performance.competency.required";
    public const string CompetencyNameRequired = "performance.competency.name_required";
    public const string QuestionRequired = "performance.question.required";
    public const string QuestionTextRequired = "performance.question.text_required";

    public const string RelationConfigInvalid = "performance.relation_config.invalid";
    public const string RelationDisabled = "performance.relation_config.disabled";
    public const string MinEvaluatorsExceedsMax = "performance.min_evaluators.exceeds_max";

    public const string ParticipantRequired = "performance.participant.required";
    public const string ParticipantAlreadyAdded = "performance.participant.already_added";
    public const string ParticipantLimitExceeded = "performance.participant.limit_exceeded";
    public const string ParticipantNotFound = "performance.participant.not_found";
    public const string SelfEvaluatorMustBeParticipant = "performance.participant.self_evaluator_must_be_participant";

    public const string AssignmentNotFound = "performance.assignment.not_found";
    public const string AssignmentNotPending = "performance.assignment.not_pending";
    public const string AssignmentAssigneeMismatch = "performance.assignment.assignee_mismatch";
    public const string ResponseAnswersIncomplete = "performance.response.answers_incomplete";
    public const string ResponseCommentRequired = "performance.response.comment_required";
    public const string ResponseUnknownQuestion = "performance.response.unknown_question";

    public const string ForceCompleteNoEligible = "performance.force_complete.no_eligible";

    public const string GoalNotEnabledForParticipant = "performance.goal.not_enabled_for_participant";
    public const string GoalCategoryRequired = "performance.goal.category_required";
    public const string GoalNameRequired = "performance.goal.name_required";
    public const string GoalScaleRequired = "performance.goal.scale_required";
    public const string GoalNotFound = "performance.goal.not_found";
    public const string GoalNotDraft = "performance.goal.not_draft";
    public const string GoalNoDraftGoals = "performance.goal.no_draft_goals";
    public const string GoalNotApproved = "performance.goal.not_approved";
    public const string GoalNoResultEnteredGoals = "performance.goal.no_result_entered_goals";
    public const string GoalResultOutOfScale = "performance.goal.result_out_of_scale";

    public const string GoalCategoryNameRequired = "performance.goal_category.name_required";
    public const string GoalCategoryNotFound = "performance.goal_category.not_found";
}

/// <summary>Domain constants and length limits.</summary>
public static class PerformanceLimits
{
    public const int NameMaxLength = 200;
    public const int TextMaxLength = 1000;
    public const int CommentMaxLength = 2000;

    public const int MaxCompetencies = 30;
    public const int MaxQuestionsPerCompetency = 30;
    public const int MaxParticipants = 2000;

    /// <summary>"Kendisi" kategorisi için min-evaluator alanı yalnız 0 (pasif) veya 1 (zorunlu) olabilir.</summary>
    public const int SelfRequiredValue = 1;
}
