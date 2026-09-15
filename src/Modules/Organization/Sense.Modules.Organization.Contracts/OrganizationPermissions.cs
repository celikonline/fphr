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

    /// <summary>Create a tenant-custom holiday calendar.</summary>
    public static readonly Permission HolidayCalendarsCreate = new(
        "organization.holiday_calendars.create", Module, "Create Holiday Calendar", Group, "Create a tenant-custom holiday calendar");

    /// <summary>Read holiday calendars and their entries.</summary>
    public static readonly Permission HolidayCalendarsRead = new(
        "organization.holiday_calendars.read", Module, "Read Holiday Calendars", Group, "View holiday calendars and their entries");

    /// <summary>Add entries to a calendar, or assign a calendar to a Company.</summary>
    public static readonly Permission HolidayCalendarsUpdate = new(
        "organization.holiday_calendars.update", Module, "Update Holiday Calendar", Group, "Add entries to a holiday calendar, or assign a calendar to a company");

    /// <summary>All Organization module permissions.</summary>
    public static IEnumerable<Permission> All =>
        [UnitsCreate, UnitsRead, UnitsUpdate, UnitsDelete, HolidayCalendarsCreate, HolidayCalendarsRead, HolidayCalendarsUpdate];
}
