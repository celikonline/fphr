using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Performance.Contracts;

/// <summary>
/// Performance module permission definitions. SystemRoleDefinitions.cs already carries "performance.*" for HR/Owner/Admin
/// (anticipated ahead of this module's build-out) and every base role already carries PermissionPatterns.SelfService
/// ("*.my.*"), so only <see cref="FormFill"/> (not a "my.*" key - an evaluator fills someone else's review) needed an
/// explicit addition to Employee/Manager's include lists.
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

    public static IEnumerable<Permission> All =>
        [CycleView, CycleCreate, CycleUpdate, CyclePublish, CycleForceComplete, ParticipantManage, FormFill, MyAssignments];
}
