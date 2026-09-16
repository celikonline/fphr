namespace Sense.Modules.Performance.Domain;

/// <summary>Domain error codes for Performance module (R0: review cycle/form + goal skeleton only).</summary>
public static class PerformanceDomainErrors
{
    public const string CycleNotFound = "performance.cycle.not_found";
    public const string CycleNameRequired = "performance.cycle.name_required";
    public const string CycleInvalidDateRange = "performance.cycle.invalid_date_range";
    public const string CycleNotActivatable = "performance.cycle.not_activatable";
    public const string CycleNotClosable = "performance.cycle.not_closable";
    public const string CycleAlreadyClosed = "performance.cycle.already_closed";

    public const string FormNotFound = "performance.form.not_found";
    public const string FormTitleRequired = "performance.form.title_required";
    public const string FormQuestionsRequired = "performance.form.questions_required";
    public const string FormQuestionTextRequired = "performance.form.question_text_required";
    public const string FormWeightsMustSumTo100 = "performance.form.weights_must_sum_to_100";
    public const string FormNotPublishable = "performance.form.not_publishable";
    public const string FormNotEditable = "performance.form.not_editable";

    public const string GoalNotFound = "performance.goal.not_found";
    public const string GoalTitleRequired = "performance.goal.title_required";
    public const string GoalProgressInvalid = "performance.goal.progress_invalid";
    public const string GoalNotUpdatable = "performance.goal.not_updatable";
    public const string GoalNotCancellable = "performance.goal.not_cancellable";
}

/// <summary>Domain constants and length limits.</summary>
public static class PerformanceLimits
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4_000;
    public const int QuestionTextMaxLength = 500;
}
