namespace Sense.Modules.Onboarding.Application;

/// <summary>Application-layer error codes for the self-service ("me") onboarding checklist.</summary>
public static class OnboardingErrors
{
    public const string NoEmployeeForUser = "onboarding.no_employee_for_user";

    /// <summary>Raised when a caller tries to view/complete another employee's materialized task via the self-service endpoints.</summary>
    public const string NotOwner = "onboarding.task.not_owner";
}
