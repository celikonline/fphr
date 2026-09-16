using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Onboarding.Contracts;

/// <summary>Onboarding module permission definitions. Not yet wired into <c>SystemRoleDefinitions</c> - ADR-0012
/// Kapı A is Domain/Application/Contracts only, no host registration this turn.</summary>
public static class OnboardingPermissions
{
    private const string Module = "onboarding";

    /// <summary>Create/edit/(de)activate onboarding and offboarding task templates.</summary>
    public static readonly Permission TemplatesManage = new(
        "onboarding.templates.manage",
        Module,
        "Manage Onboarding Templates",
        "Onboarding",
        "Create, edit and (de)activate onboarding/offboarding task templates");

    /// <summary>View employees' materialized onboarding/offboarding task checklists.</summary>
    public static readonly Permission TasksView = new(
        "onboarding.tasks.view",
        Module,
        "View Onboarding Tasks",
        "Onboarding",
        "View employees' onboarding/offboarding task checklists");

    /// <summary>Materialize, complete and waive employees' onboarding/offboarding tasks.</summary>
    public static readonly Permission TasksManage = new(
        "onboarding.tasks.manage",
        Module,
        "Manage Onboarding Tasks",
        "Onboarding",
        "Materialize, complete and waive employees' onboarding/offboarding tasks");

    /// <summary>All Onboarding module permissions.</summary>
    public static IEnumerable<Permission> All =>
    [
        TemplatesManage,
        TasksView,
        TasksManage,
    ];
}
