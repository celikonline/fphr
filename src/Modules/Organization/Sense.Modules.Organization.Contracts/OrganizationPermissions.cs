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
    private const string Group = "Şirket Yapısı";

    /// <summary>Create an org unit (any hierarchy level).</summary>
    public static readonly Permission UnitsCreate = new(
        "organization.units.create", Module, "Birim Oluşturma", Group, "Herhangi bir hiyerarşi seviyesinde organizasyon birimi oluşturma");

    /// <summary>Read org units, levels (types), and the org tree.</summary>
    public static readonly Permission UnitsRead = new(
        "organization.units.read", Module, "Birimleri Görüntüleme", Group, "Organizasyon birimlerini, hiyerarşi seviyelerini ve organizasyon ağacını görüntüleme");

    /// <summary>Rename/reparent/assign manager or location to an org unit.</summary>
    public static readonly Permission UnitsUpdate = new(
        "organization.units.update", Module, "Birim Güncelleme", Group, "Bir organizasyon birimini yeniden adlandırma, üst birimini değiştirme veya yönetici/lokasyon atama");

    /// <summary>Deactivate (soft delete) an org unit.</summary>
    public static readonly Permission UnitsDelete = new(
        "organization.units.delete", Module, "Birim Pasifleştirme", Group, "Bir organizasyon birimini pasifleştirme");

    /// <summary>Create a tenant-custom holiday calendar.</summary>
    public static readonly Permission HolidayCalendarsCreate = new(
        "organization.holiday_calendars.create", Module, "Tatil Takvimi Oluşturma", Group, "Kiracıya özel bir tatil takvimi oluşturma");

    /// <summary>Read holiday calendars and their entries.</summary>
    public static readonly Permission HolidayCalendarsRead = new(
        "organization.holiday_calendars.read", Module, "Tatil Takvimlerini Görüntüleme", Group, "Tatil takvimlerini ve içerdikleri günleri görüntüleme");

    /// <summary>Rename a calendar, add/edit/remove its entries, or assign a calendar to a Company.</summary>
    public static readonly Permission HolidayCalendarsUpdate = new(
        "organization.holiday_calendars.update", Module, "Tatil Takvimi Güncelleme", Group, "Bir tatil takvimini yeniden adlandırma, gün ekleme/düzenleme/silme veya bir şirkete takvim atama");

    /// <summary>Delete a tenant-custom holiday calendar (not the system calendar, not one assigned to a Company).</summary>
    public static readonly Permission HolidayCalendarsDelete = new(
        "organization.holiday_calendars.delete", Module, "Tatil Takvimi Silme", Group, "Kiracıya özel bir tatil takvimini silme");

    /// <summary>Create a location (roadmap Faz 0 item #6: office/store/warehouse/factory/remote + country).</summary>
    public static readonly Permission LocationsCreate = new(
        "organization.locations.create", Module, "Lokasyon Oluşturma", Group, "Fiziksel bir lokasyon oluşturma");

    /// <summary>Read locations.</summary>
    public static readonly Permission LocationsRead = new(
        "organization.locations.read", Module, "Lokasyonları Görüntüleme", Group, "Lokasyonları görüntüleme");

    /// <summary>Rename/retype/re-address a location.</summary>
    public static readonly Permission LocationsUpdate = new(
        "organization.locations.update", Module, "Lokasyon Güncelleme", Group, "Bir lokasyonu yeniden adlandırma, türünü veya adresini değiştirme");

    /// <summary>Deactivate (soft delete) a location.</summary>
    public static readonly Permission LocationsDelete = new(
        "organization.locations.delete", Module, "Lokasyon Pasifleştirme", Group, "Bir lokasyonu pasifleştirme");

    /// <summary>Add, activate, rename, reorder, and remove hierarchy levels.</summary>
    public static readonly Permission UnitTypesManage = new(
        UnitTypesManageKey, Module, "Hiyerarşi Seviyesi Yönetimi", Group, "Organizasyon hiyerarşi seviyeleri ekleme, yeniden adlandırma, sıralama, etkinleştirme veya kaldırma");

    /// <summary>Create a company (Company-level org unit with its holiday calendar).</summary>
    public static readonly Permission CompaniesCreate = new(
        CompaniesCreateKey, Module, "Şirket Oluşturma", Group, "Tatil takvimiyle birlikte bir şirket oluşturma");

    /// <summary>Edit the tenant's company profile (legal/registry fields, operating hours, logo). Faz 2, TB-10.</summary>
    public static readonly Permission CompanyProfileManage = new(
        CompanyProfileManageKey, Module, "Şirket Profili Yönetimi", Group, "Şirket profilini düzenleme: yasal/sicil alanları, çalışma saatleri ve logo");

    /// <summary>View the effective upcoming holidays (home page list).</summary>
    public static readonly Permission HolidaysView = new(
        HolidaysViewKey, Module, "Tatilleri Görüntüleme", Group, "Yaklaşan resmi tatilleri görüntüleme");

    /// <summary>Assign/clear the approval policy per request type on an org unit ("Opsiyonel alanlar").</summary>
    public static readonly Permission ApprovalAssignmentsManage = new(
        ApprovalAssignmentsManageKey, Module, "Birim Onay Politikası Yönetimi", Group, "Bir organizasyon biriminde talep türü başına onay politikası atama veya kaldırma");

    /// <summary>View the cost center catalog (S-4, roadmap Faz 2 item #2).</summary>
    public static readonly Permission CostCentersView = new(
        CostCentersViewKey, Module, "Masraf Merkezlerini Görüntüleme", Group, "Masraf merkezi kataloğunu görüntüleme");

    /// <summary>Create, rename, and activate/deactivate cost centers (S-4).</summary>
    public static readonly Permission CostCentersManage = new(
        CostCentersManageKey, Module, "Masraf Merkezi Yönetimi", Group, "Masraf merkezi oluşturma, yeniden adlandırma ve etkinleştirme/pasifleştirme");

    public const string UnitTypesManageKey = "organization.unit_types.manage";
    public const string CompaniesCreateKey = "organization.companies.create";
    public const string CompanyProfileManageKey = "organization.company_profile.manage";
    public const string HolidaysViewKey = "organization.holidays.view";
    public const string ApprovalAssignmentsManageKey = "organization.approval_assignments.manage";
    public const string CostCentersViewKey = "organization.cost_centers.view";
    public const string CostCentersManageKey = "organization.cost_centers.manage";

    /// <summary>All Organization module permissions.</summary>
    public static IEnumerable<Permission> All =>
        [UnitsCreate, UnitsRead, UnitsUpdate, UnitsDelete,
         HolidayCalendarsCreate, HolidayCalendarsRead, HolidayCalendarsUpdate, HolidayCalendarsDelete,
         LocationsCreate, LocationsRead, LocationsUpdate, LocationsDelete,
         UnitTypesManage, CompaniesCreate, CompanyProfileManage, HolidaysView, ApprovalAssignmentsManage,
         CostCentersView, CostCentersManage];
}
