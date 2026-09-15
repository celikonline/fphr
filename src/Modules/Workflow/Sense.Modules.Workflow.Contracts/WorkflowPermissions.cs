using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Workflow.Contracts;

/// <summary>
/// Workflow module permission definitions. SystemRoleDefinitions.cs already anticipates these prefixes -
/// HR/HRSpecialist carry "workflow.*", Manager carries "workflow.inbox.*", and every base role carries
/// PermissionPatterns.SelfService ("*.my.*") - so no SystemRoleDefinitions.cs change is needed for the keys below.
/// </summary>
public static class WorkflowPermissions
{
    private const string Module = "workflow";
    private const string Group = "Workflow";

    /// <summary>Read approval policies (name, request type, steps).</summary>
    public static readonly Permission PoliciesRead = new(
        "workflow.policies.read", Module, "Read Approval Policies", Group, "View approval policies and their steps");

    /// <summary>Create a named approval policy.</summary>
    public static readonly Permission PoliciesCreate = new(
        "workflow.policies.create", Module, "Create Approval Policy", Group, "Create a named approval policy");

    /// <summary>Rename/replace steps/toggle visibility on an approval policy.</summary>
    public static readonly Permission PoliciesUpdate = new(
        "workflow.policies.update", Module, "Update Approval Policy", Group, "Rename, replace the step list of, or change the visibility of an approval policy");

    /// <summary>Deactivate (soft delete) an approval policy.</summary>
    public static readonly Permission PoliciesDelete = new(
        "workflow.policies.delete", Module, "Deactivate Approval Policy", Group, "Deactivate an approval policy");

    /// <summary>View pending approval tasks assigned to the caller (directly, or via a fallback role queue).</summary>
    public static readonly Permission InboxView = new(
        "workflow.inbox.view", Module, "View Approval Inbox", Group, "View pending approval tasks assigned to the caller");

    /// <summary>Decide (approve/reject) a pending task the caller is the assignee of, or is authorized for via a fallback role queue.</summary>
    public static readonly Permission InboxDecide = new(
        "workflow.inbox.decide", Module, "Decide Approval Task", Group, "Approve or reject a pending approval task assigned to the caller");

    /// <summary>Decide a pending task on behalf of someone else, or cancel any approval request (Admin override).</summary>
    public static readonly Permission RequestOverride = new(
        "workflow.request.override", Module, "Override Approval Decision", Group, "Decide a pending approval task on behalf of someone else, or cancel any approval request");

    /// <summary>View the status/history of one's own submitted approval requests.</summary>
    public static readonly Permission MyRequests = new(
        "workflow.my.requests", Module, "View My Approval Requests", Group, "View the status and history of the caller's own submitted approval requests");

    public static IEnumerable<Permission> All =>
        [PoliciesRead, PoliciesCreate, PoliciesUpdate, PoliciesDelete, InboxView, InboxDecide, RequestOverride, MyRequests];
}
