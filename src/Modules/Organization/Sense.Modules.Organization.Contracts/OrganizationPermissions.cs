using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Organization module permission definitions.
/// Note on naming: the permission-key module prefix is "organization" (matching
/// docs/analysis/kolayik-video/11.3.02-sirket-yapisi.md: "Permission anahtarları
/// (OrganizationPermissions.Module="organization"; DB şeması `org` değişmez)"), while the module's
/// DbContext schema / IModule.Name stay "org" (unchanged from the pre-existing Faz 0 skeleton,
/// see Sense.Modules.Organization.Api.OrganizationModule and Sense.Modules.Organization.Infrastructure
/// .Persistence.OrganizationDbContext.SchemaName). These are deliberately different strings per the
/// design doc's own note - do not "fix" one to match the other without re-checking that doc.
/// Known gap: Identity's SystemRoleDefinitions.cs (src/Modules/Identity/.../Roles/SystemRoleDefinitions.cs)
/// currently grants roles like HR/Manager/Timekeeper/Auditor a wildcard "org.*", which will NOT match
/// these "organization.*" keys (the wildcard-matching regex requires a literal "org." prefix). That
/// file is Identity's, out of scope for this item - reconciling the wildcard is left to a follow-up.
/// </summary>
public static class OrganizationPermissions
{
    private const string Module = "organization";
    private const string Group = "Organization";

    /// <summary>Create an org unit (any hierarchy level).</summary>
    public static readonly Permission UnitsCreate = new(
        "organization.units.create", Module, "Create Org Unit", Group, "Create an organization unit at any hierarchy level");

    /// <summary>Read org units, levels (types), and the org tree.</summary>
    public static readonly Permission UnitsRead = new(
        "organization.units.read", Module, "Read Org Units", Group, "View organization units, hierarchy levels, and the org tree");

    /// <summary>Rename/reparent/assign manager or location to an org unit.</summary>
    public static readonly Permission UnitsUpdate = new(
        "organization.units.update", Module, "Update Org Unit", Group, "Rename, reparent, or assign a manager/location to an organization unit");

    /// <summary>Deactivate (soft delete) an org unit.</summary>
    public static readonly Permission UnitsDelete = new(
        "organization.units.delete", Module, "Deactivate Org Unit", Group, "Deactivate an organization unit");

    /// <summary>All Organization module permissions.</summary>
    public static IEnumerable<Permission> All => [UnitsCreate, UnitsRead, UnitsUpdate, UnitsDelete];
}
