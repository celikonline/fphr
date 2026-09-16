using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Performance.Contracts;

/// <summary>
/// Performance module permission definitions. SystemRoleDefinitions.cs already carries "performance.*" for HR/Owner/Admin
/// and "performance.goals.*" for Manager (anticipated ahead of this module's build-out - the "goals" (plural) resource
/// segment matches that pre-existing pattern on purpose), and every base role already carries PermissionPatterns.SelfService
/// ("*.my.*"). Only <see cref="FormFill"/> and Employee's goal-entry keys (not "my.*"/"goals.*" - an evaluator fills
/// someone else's review, an employee writes to a participant record they don't own) needed explicit additions.
/// </summary>
public static class PerformancePermissions
{
    private const string Module = "performance";
    private const string Group = "Performance";

    /// <summary>List/read review cycles and their participant progress.</summary>
    public static readonly Permission CycleView = new(
        "performance.cycle.view", Module, "View Review Cycles", Group, "View review cycles and participant progress");

    /// <summary>Create a review cycle (sihirbaz "Kaydet ve bitir").</summary>
    public static readonly Permission CycleCreate = new(
        "performance.cycle.create", Module, "Create Review Cycle", Group, "Create a review cycle");

    /// <summary>Edit a review cycle while still Draft.</summary>
    public static readonly Permission CycleUpdate = new(
        "performance.cycle.update", Module, "Update Review Cycle", Group, "Edit a review cycle while it is still in Draft status");

    /// <summary>Draft → Published: opens the cycle's forms for evaluators.</summary>
    public static readonly Permission CyclePublish = new(
        "performance.cycle.publish", Module, "Publish Review Cycle", Group, "Publish a review cycle, opening its forms for evaluators");

    /// <summary>"Değerlendirmeyi tamamla": force-complete eligible participants.</summary>
    public static readonly Permission CycleForceComplete = new(
        "performance.cycle.force_complete", Module, "Force-Complete Review Cycle", Group, "Force-complete eligible participants of a review cycle");

    /// <summary>Add participants (with their per-category evaluators) to a Draft cycle.</summary>
    public static readonly Permission ParticipantManage = new(
        "performance.participant.manage", Module, "Manage Review Participants", Group, "Add participants and their evaluators to a review cycle");

    /// <summary>Fill in an assigned evaluation form (self, manager, subordinate, or peer relation).</summary>
    public static readonly Permission FormFill = new(
        "performance.form.fill", Module, "Fill Evaluation Form", Group, "Submit an assigned evaluation form");

    /// <summary>View the caller's own assigned forms and results ("Bana atanan formlar", "Sonuçlarım").</summary>
    public static readonly Permission MyAssignments = new(
        "performance.my.assignments", Module, "View My Assignments", Group, "View the caller's own assigned evaluation forms and results");

    /// <summary>Add a goal for a participant ("Hedef ekle" - by the employee or their first manager, per GoalEntryBy).</summary>
    public static readonly Permission GoalsCreate = new(
        "performance.goals.create", Module, "Add Goal", Group, "Add a goal for a review participant");

    /// <summary>"Hedefleri onayla": bulk-approve a participant's Draft goals.</summary>
    public static readonly Permission GoalsApprove = new(
        "performance.goals.approve", Module, "Approve Goals", Group, "Approve a review participant's draft goals");

    /// <summary>"Sonuç gir": record a goal's actual value.</summary>
    public static readonly Permission GoalsEnterResult = new(
        "performance.goals.enter_result", Module, "Enter Goal Result", Group, "Enter a goal's actual result");

    /// <summary>"Sonuçları onayla": bulk-approve a participant's entered goal results.</summary>
    public static readonly Permission GoalsApproveResult = new(
        "performance.goals.approve_result", Module, "Approve Goal Results", Group, "Approve a review participant's entered goal results");

    /// <summary>Manage the tenant-defined goal category list.</summary>
    public static readonly Permission GoalCategoryManage = new(
        "performance.goal_category.manage", Module, "Manage Goal Categories", Group, "Create and list goal categories");

    public static IEnumerable<Permission> All =>
    [
        CycleView, CycleCreate, CycleUpdate, CyclePublish, CycleForceComplete, ParticipantManage, FormFill, MyAssignments,
        GoalsCreate, GoalsApprove, GoalsEnterResult, GoalsApproveResult, GoalCategoryManage,
    ];
}
