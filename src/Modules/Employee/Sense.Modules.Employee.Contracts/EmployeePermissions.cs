using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Contracts;

/// <summary>Employee module permission definitions.</summary>
public static class EmployeePermissions
{
    private const string Module = "employee";
    private const string Group = "Özlük";

    public const string PersonnelNumberManageKey = "employee.personnel_number.manage";
    public static readonly Permission PersonnelNumberManage = new(PersonnelNumberManageKey, Module, "Sicil Numarası Yönetimi", Group, "Sicil aralıkları ve şirket atama kurallarını yönetme");
    public const string EmployeesReadKey = "employee.employees.read";
    public const string EmployeesCreateKey = "employee.employees.create";
    public const string EmployeesUpdateKey = "employee.employees.update";
    public const string EmployeesBulkUpdateKey = "employee.employees.bulk_update";
    public const string DirectoryViewKey = "employee.directory.view";
    public const string OrgChartViewKey = "employee.orgchart.view";
    public const string SalaryViewKey = "employee.salary.view";

    /// <summary>Create/update/delete salary records (on top of <see cref="SalaryViewKey"/>).</summary>
    public const string SalaryEditKey = "employee.salary.edit";

    /// <summary>Create/update/delete bank accounts and pick the primary one; reading them needs only employees.read (IBAN masked without sensitive.view).</summary>
    public const string BankAccountsEditKey = "employee.bank_accounts.edit";
    public const string MyViewKey = "employee.my.view";
    public const string EmployeesImportKey = "employee.employees.import";

    /// <summary>Unmasked TCKN, IBAN and health/disability fields (DisabilityDegree, BloodType, MilitaryStatus); everyone else gets masked/null.</summary>
    public const string SensitiveViewKey = "employee.sensitive.view";

    /// <summary>Create/update/deactivate/reactivate custom field definitions (tenant admin catalog).</summary>
    public const string CustomFieldManageKey = "employee.customfield.manage";

    /// <summary>List custom field definitions (on top of employees.read for reading employees' values).</summary>
    public const string CustomFieldViewKey = "employee.customfield.view";

    /// <summary>Create/update/deactivate termination reasons (tenant catalog; reading them needs only employees.read).</summary>
    public const string TerminationReasonManageKey = "employee.termination_reason.manage";

    /// <summary>Union memberships - special-category personal data (KVKK md.6), so they have their own read permission.</summary>
    public const string UnionViewKey = "employee.union.view";

    /// <summary>Add/edit/delete union memberships.</summary>
    public const string UnionManageKey = "employee.union.manage";

    /// <summary>Maintain the subcontractor ("alt işveren") catalog; reading it needs only employees.read.</summary>
    public const string SubcontractorManageKey = "employee.subcontractor.manage";

    /// <summary>
    /// Self-service edit of one's own contact/address fields and employee-editable custom field values
    /// ("Self-servis düzenleme", 11.3.03). Contains ".self." so the wildcard role wiring (PermissionPatterns.SelfService)
    /// grants it automatically. Exactly module.resource.action (3 segments) per the architecture rule
    /// (Sense.Tests.Architecture.OnionArchitectureTests.PermissionKeys_FollowNamingConvention).
    /// </summary>
    public const string ProfileSelfEditKey = "employee.self.edit";

    /// <summary>
    /// FG-007 "Ekibim": the caller's own direct reports (ManagerEmployeeId == caller), not the Team data scope. "*.my.*" so every
    /// base role gets it through PermissionPatterns.SelfService - a caller who manages nobody simply gets an empty list.
    /// </summary>
    public const string MyTeamKey = "employee.my.team";

    /// <summary>Read employee records (list/detail), filtered by data scope.</summary>
    public static readonly Permission EmployeesRead = new(
        EmployeesReadKey, Module, "Çalışanları Görüntüleme", Group, "Çalışan kayıtlarını görüntüleme");

    /// <summary>Create an employee record.</summary>
    public static readonly Permission EmployeesCreate = new(
        EmployeesCreateKey, Module, "Çalışan Oluşturma", Group, "Çalışan kaydı oluşturma");

    /// <summary>Update an employee record.</summary>
    public static readonly Permission EmployeesUpdate = new(
        EmployeesUpdateKey, Module, "Çalışan Güncelleme", Group, "Çalışan kaydını güncelleme");

    /// <summary>Bulk operations over an employee selection (e.g. assign manager).</summary>
    public static readonly Permission EmployeesBulkUpdate = new(
        EmployeesBulkUpdateKey, Module, "Toplu Çalışan Güncelleme", Group, "Seçilen çalışanlar üzerinde toplu işlem çalıştırma");

    /// <summary>Company directory (name, unit, work contact).</summary>
    public static readonly Permission DirectoryView = new(
        DirectoryViewKey, Module, "Rehberi Görüntüleme", Group, "Şirket çalışan rehberini ve yaklaşan doğum günlerini görüntüleme");

    /// <summary>Organization chart by manager hierarchy.</summary>
    public static readonly Permission OrgChartView = new(
        OrgChartViewKey, Module, "Organizasyon Şemasını Görüntüleme", Group, "Organizasyon şemasını görüntüleme");

    /// <summary>Salary history (card "Ödemeler" tab); unit managers get it only via the unit authorization's salary switch.</summary>
    public static readonly Permission SalaryView = new(
        SalaryViewKey, Module, "Maaşı Görüntüleme", Group, "Maaş ve ödeme bilgilerini görüntüleme");

    /// <summary>Maintain salary history.</summary>
    public static readonly Permission SalaryEdit = new(
        SalaryEditKey, Module, "Maaş Düzenleme", Group, "Maaş kayıtları ekleme, düzenleme ve silme");

    /// <summary>Maintain bank accounts.</summary>
    public static readonly Permission BankAccountsEdit = new(
        BankAccountsEditKey, Module, "Banka Hesabı Düzenleme", Group, "Banka hesabı ekleme, düzenleme, silme ve birincil hesabı seçme");

    /// <summary>Own employee card (self-service; matches the "*.my.*" pattern of self-service roles).</summary>
    public static readonly Permission MyView = new(
        MyViewKey, Module, "Kendi Özlük Kartımı Görüntüleme", Group, "Kendi çalışan kaydınızı görüntüleme");

    /// <summary>Run the "employee.master" data import template (on top of datatransfer.import.run).</summary>
    public static readonly Permission EmployeesImport = new(
        EmployeesImportKey, Module, "Çalışan İçe Aktarma", Group, "İçe aktarma dosyasından çalışan kaydı ekleme veya güncelleme");

    /// <summary>Unmasked TCKN, IBAN and health/disability fields.</summary>
    public static readonly Permission SensitiveView = new(
        SensitiveViewKey, Module, "Hassas Alanları Görüntüleme", Group, "Maskesiz T.C. kimlik no, IBAN ve sağlık/engellilik alanlarını görüntüleme");

    /// <summary>Maintain custom field definitions.</summary>
    public static readonly Permission CustomFieldManage = new(
        CustomFieldManageKey, Module, "Özel Alan Yönetimi", Group, "Özel alan tanımları oluşturma, güncelleme, pasifleştirme ve yeniden etkinleştirme");

    /// <summary>List custom field definitions.</summary>
    public static readonly Permission CustomFieldView = new(
        CustomFieldViewKey, Module, "Özel Alan Tanımlarını Görüntüleme", Group, "Özel alan tanımı kataloğunu görüntüleme");

    /// <summary>Maintain the termination reason catalog.</summary>
    public static readonly Permission TerminationReasonManage = new(
        TerminationReasonManageKey, Module, "Fesih Nedeni Yönetimi", Group, "Fesih nedenleri oluşturma, yeniden adlandırma, pasifleştirme ve yeniden etkinleştirme");

    /// <summary>View union memberships.</summary>
    public static readonly Permission UnionView = new(
        UnionViewKey, Module, "Sendika Üyeliğini Görüntüleme", Group, "Sendika üyeliği bilgilerini görüntüleme (özel nitelikli veri)");

    /// <summary>Maintain union memberships.</summary>
    public static readonly Permission UnionManage = new(
        UnionManageKey, Module, "Sendika Üyeliği Yönetimi", Group, "Sendika üyeliği ekleme, düzenleme ve silme");

    /// <summary>Maintain the subcontractor catalog.</summary>
    public static readonly Permission SubcontractorManage = new(
        SubcontractorManageKey, Module, "Alt İşveren Yönetimi", Group, "Alt işveren firmaları oluşturma, düzenleme, pasifleştirme ve yeniden etkinleştirme");

    /// <summary>Self-service profile edit (own contact fields + own employee-editable custom fields).</summary>
    public static readonly Permission ProfileSelfEdit = new(
        ProfileSelfEditKey, Module, "Kendi Profilimi Düzenleme", Group, "Kendi iletişim alanlarınızı ve çalışan tarafından düzenlenebilir özel alanlarınızı düzenleme");

    public static readonly Permission NotesView = new(
        "employee.notes.view", Module, "Çalışan Notlarını Görüntüleme", Group, "Çalışan kartındaki İK/yönetici notlarını görüntüleme");

    public static readonly Permission NotesManage = new(
        "employee.notes.manage", Module, "Çalışan Notu Yönetimi", Group, "Çalışan notu ekleme, düzenleme ve silme");

    /// <summary>"Ekibim": own direct reports (FG-007).</summary>
    public static readonly Permission MyTeam = new(
        MyTeamKey, Module, "Ekibimi Görüntüleme", Group, "Size doğrudan bağlı çalışanların listesini görüntüleme");

    /// <summary>All Employee module permissions.</summary>
    public static IEnumerable<Permission> All =>
        [
            EmployeesRead, EmployeesCreate, EmployeesUpdate, EmployeesBulkUpdate, DirectoryView, OrgChartView, SalaryView, SalaryEdit,
            BankAccountsEdit, MyView, EmployeesImport, SensitiveView, CustomFieldManage, CustomFieldView, TerminationReasonManage, UnionView, UnionManage, SubcontractorManage, ProfileSelfEdit,
            NotesView, NotesManage,
            MyTeam, PersonnelNumberManage,
        ];
}
