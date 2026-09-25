namespace Sense.Modules.Onboarding.Domain;

/// <summary>Domain error codes for the Onboarding module.</summary>
public static class OnboardingDomainErrors
{
    public const string TaskTemplateNotFound = "onboarding_task_template.not_found";
    public const string TaskTemplateTitleRequired = "onboarding_task_template.title_required";
    public const string TaskTemplateTitleTooLong = "onboarding_task_template.title_too_long";
    public const string TaskTemplateResponsibleRoleRequired = "onboarding_task_template.responsible_role_required";

    public const string EmployeeTaskNotFound = "employee_onboarding_task.not_found";

    /// <summary>Completed/Waived tasks are final - raised by both <c>Complete</c> and <c>Waive</c> once the task
    /// has already left the <c>Pending</c> status.</summary>
    public const string EmployeeTaskAlreadyFinalized = "employee_onboarding_task.already_finalized";

    public const string EmployeeTaskWaiveReasonRequired = "employee_onboarding_task.waive_reason_required";

    public const string CategoryNotFound = "onboarding_category.not_found";
    public const string CategoryNameRequired = "onboarding_category.name_required";
    public const string CategoryNameTooLong = "onboarding_category.name_too_long";

    public const string TemplateSetNotFound = "onboarding_template_set.not_found";
    public const string TemplateSetNameRequired = "onboarding_template_set.name_required";
    public const string TemplateSetNameTooLong = "onboarding_template_set.name_too_long";
    public const string TemplateSetEmpty = "onboarding_template_set.empty";
    public const string TemplateSetInactive = "onboarding_template_set.inactive";
    public const string TemplateSetUnknownTemplate = "onboarding_template_set.unknown_template";
    public const string TemplateSetAlreadyStarted = "onboarding_template_set.already_started";
}

/// <summary>Domain constants and limits.</summary>
public static class OnboardingLimits
{
    public const int TaskTemplateTitleMaxLength = 200;

    public const int WaivedReasonMaxLength = 500;
    public const int ResponsibleRoleCodeMaxLength = 50;
    public const int CategoryNameMaxLength = 100;
    public const int TemplateSetNameMaxLength = 150;
}
