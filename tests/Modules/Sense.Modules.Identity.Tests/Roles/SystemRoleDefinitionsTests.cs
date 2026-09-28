using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Identity.Tests.Roles;

/// <summary>
/// Dört kullanıcı grubu modeli: İK Yöneticisi (HR) ve Muhasebe (PayrollSpecialist) şirket içinde her şeyi yapar (platform
/// izinleri hariç), Çalışan yalnız self-servis + dizin/organizasyon şeması; süper admin yalnız şirket açar.
/// </summary>
public sealed class SystemRoleDefinitionsTests
{
    private static readonly string[] SelfServiceKeys =
        ["employee.self.edit", "employee.my.view", "attendance.my.timesheet", "expense.my.claims", "me.profile.view",
         "attendance.shift.self", "attendance.availability.self", "payroll.my.payslip"];

    private static readonly IReadOnlyList<Permission> Catalog =
    [
        .. new[]
        {
            "employee.employees.read", "employee.employees.create", "employee.employees.update", "employee.employees.bulk_update",
            "employee.directory.view", "employee.orgchart.view", "employee.salary.view", "employee.salary.edit", "employee.bank_accounts.edit",
            "employee.sensitive.view", "organization.holidays.view", "organization.units.read",
            "organization.approval_assignments.manage", "billing.invoices.view", "analytics.kpi.view",
            "identity.users.view", "identity.users.manage", "identity.users.invite", "identity.users.suspend",
            "identity.users.reset_mfa", "identity.users.impersonate", "identity.roles.view", "identity.modules.manage",
            "attendance.timesheet.view", "attendance.timesheet.resolve", "attendance.timesheet.approve",
            "attendance.shift.view", "attendance.availability.view",
            "payroll.runs.approve", "payroll.salary.view", "payroll.trace.view", "expense.categories.view", "expense.categories.manage",
            "workflow.inbox.view", "workflow.inbox.decide", "workflow.request.override", "workflow.policies.create",
            PlatformPermissions.TenantsManage,
        }
        .Concat(SelfServiceKeys)
        .Select(k => new Permission(k, k.Split('.')[0], k, "Test")),
    ];

    private static readonly string[] NonPlatformKeys =
        [.. Catalog.Select(p => p.Key).Where(k => !PlatformPermissions.IsPlatformKey(k))];

    [Fact]
    public void Definitions_CoverExactlyTheCustomerSystemRoles()
    {
        SystemRoleDefinitions.All.Select(d => d.Code).ShouldBe(SystemRoleCodes.All, ignoreOrder: true);
    }

    [Theory]
    [InlineData(SystemRoleCodes.Owner)]
    [InlineData(SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.PayrollSpecialist)]
    public void FullAccessGroups_GetEveryNonPlatformKey_AtCompanyScope(string roleCode)
    {
        var granted = SystemRoleDefinitions.Get(roleCode).Resolve(Guid.NewGuid(), Catalog).ToList();

        granted.Select(p => p.Permission).ShouldBe(NonPlatformKeys, ignoreOrder: true);
        granted.ShouldAllBe(p => p.Scope == (int)DataScope.Company);
        granted.ShouldNotContain(p => PlatformPermissions.IsPlatformKey(p.Permission));
    }

    [Theory]
    [InlineData(SystemRoleCodes.HR)]
    [InlineData(SystemRoleCodes.PayrollSpecialist)]
    public void HrAndPayroll_GetFormerlyRestrictedKeys(string roleCode)
    {
        // Eski modelde İK Yöneticisinden/uzmanlardan alınan kimlik yönetimi, maaş/banka ve onay anahtarları artık iki grupta da var.
        var keys = Resolve(roleCode);

        keys.ShouldContain("identity.users.manage");
        keys.ShouldContain("identity.users.impersonate");
        keys.ShouldContain("employee.salary.edit");
        keys.ShouldContain("employee.bank_accounts.edit");
        keys.ShouldContain("billing.invoices.view");
        keys.ShouldContain("organization.approval_assignments.manage");
        keys.ShouldContain("attendance.timesheet.approve");
    }

    [Fact]
    public void Employee_GetsSelfServiceDirectoryOrgChartAndHolidays_AtSelfScope()
    {
        var keys = Resolve(SystemRoleCodes.Employee);

        SystemRoleDefinitions.Get(SystemRoleCodes.Employee).Scope.ShouldBe(DataScope.Self);
        keys.ShouldBe(
            [.. SelfServiceKeys, "employee.directory.view", "employee.orgchart.view", "organization.holidays.view", "expense.categories.view",
             "workflow.inbox.view", "workflow.inbox.decide"],
            ignoreOrder: true);
    }

    [Fact]
    public void Employee_GetsNoEmployeeRecordsSalaryIdentityOrPlatformKeys()
    {
        var keys = Resolve(SystemRoleCodes.Employee);

        keys.ShouldNotContain(k => k.StartsWith("employee.employees.", StringComparison.Ordinal));
        keys.ShouldNotContain(k => k.Contains(".salary.", StringComparison.Ordinal));
        keys.ShouldNotContain(k => k.StartsWith("identity.", StringComparison.Ordinal));
        keys.ShouldNotContain("employee.bank_accounts.edit");
        keys.ShouldNotContain("employee.sensitive.view");
        keys.ShouldNotContain("attendance.timesheet.approve");
        keys.ShouldNotContain(PlatformPermissions.TenantsManage);
    }

    /// <summary>Çalışan "*.my.*" deseniyle kendi bordro pusulalarını görme iznini otomatik alır, ama bu
    /// KISA YOL DEĞİL - dönemin tüm bordrolarını açan payroll.trace.view'ı almaz (kapsam açığı ayrı konu).</summary>
    [Fact]
    public void Employee_GetsOwnPayslipsPermission_ButNotTheTenantWideTraceViewPermission()
    {
        var keys = Resolve(SystemRoleCodes.Employee);

        keys.ShouldContain("payroll.my.payslip");
        keys.ShouldNotContain("payroll.trace.view");
    }

    /// <summary>Mobil bulgu: "*.self.*" yalnız ".self"ten sonra segment olan anahtarları eşliyordu, ".self" ile BİTEN
    /// tek desenli anahtarları (vardiyam/müsaitliğim) hiçbir sistem rolüne vermiyordu. "*.self" deseni eklendi.</summary>
    [Theory]
    [InlineData(SystemRoleCodes.Employee)]
    [InlineData(SystemRoleCodes.Manager)]
    public void SelfServiceRoles_GetShiftAndAvailabilitySelfKeys_ButNotTheTeamWideViewKeys(string roleCode)
    {
        var keys = Resolve(roleCode);

        keys.ShouldContain("attendance.shift.self");
        keys.ShouldContain("attendance.availability.self");
        keys.ShouldNotContain(PlatformPermissions.TenantsManage);
    }

    [Fact]
    public void Employee_GetsShiftAndAvailabilitySelfKeys_ButNotTheOthersViewKeys()
    {
        var keys = Resolve(SystemRoleCodes.Employee);

        keys.ShouldContain("attendance.shift.self");
        keys.ShouldContain("attendance.availability.self");
        keys.ShouldNotContain("attendance.shift.view");
        keys.ShouldNotContain("attendance.availability.view");
    }

    /// <summary>Ekip yöneticileri Çalışan grubunda: kendilerine atanan onayları görüp karar verebilmeli, ama başkası adına
    /// karar veremez ve onay politikası yönetemez.</summary>
    [Fact]
    public void Employee_CanSeeAndDecideTheirOwnApprovalTasks_ButNotOverrideOrManagePolicies()
    {
        var keys = Resolve(SystemRoleCodes.Employee);

        keys.ShouldContain("workflow.inbox.view");
        keys.ShouldContain("workflow.inbox.decide");
        keys.ShouldNotContain("workflow.request.override");
        keys.ShouldNotContain("workflow.policies.create");
    }

    /// <summary>Müdür: ekip kapsamında izin/harcama/puantaj onayı ve ekip görünümü; maaş, kimlik yönetimi, platform yok.</summary>
    [Fact]
    public void Manager_ApprovesTheTeamsRequestsAtTeamScope_ButGetsNoSalaryIdentityOrEmployeeWrites()
    {
        var keys = Resolve(SystemRoleCodes.Manager);

        SystemRoleDefinitions.Get(SystemRoleCodes.Manager).Scope.ShouldBe(DataScope.Team);
        keys.ShouldContain("workflow.inbox.view");
        keys.ShouldContain("workflow.inbox.decide");
        keys.ShouldContain("attendance.timesheet.approve");
        keys.ShouldContain("attendance.timesheet.view");
        keys.ShouldContain("employee.directory.view");
        keys.ShouldNotContain("workflow.request.override");
        keys.ShouldNotContain(k => k.Contains(".salary.", StringComparison.Ordinal));
        keys.ShouldNotContain("employee.sensitive.view");
        keys.ShouldNotContain(k => k.StartsWith("identity.", StringComparison.Ordinal));
        keys.ShouldNotContain("employee.employees.update");
        keys.ShouldNotContain(PlatformPermissions.TenantsManage);
    }

    /// <summary>QA teslim testi bulgu 12: çalışan masraf kalemi eklerken kategori listesini okur (yalnız okuma).</summary>
    [Fact]
    public void Employee_CanReadTheExpenseCategoryCatalog_ButNotManageIt()
    {
        var keys = Resolve(SystemRoleCodes.Employee);

        keys.ShouldContain("expense.my.claims");
        keys.ShouldContain("expense.categories.view");
        keys.ShouldNotContain("expense.categories.manage");
    }

    [Fact]
    public void SuperAdmin_ResolvesToExactlyTenantCreation_AndNothingTenantLevel()
    {
        // "Süper admin yalnız şirket açabilir": gerçek kimlik kataloğu eklense bile yalnız platform.tenants.manage.
        var keys = SystemRoleDefinitions.SuperAdmin.Resolve(Guid.NewGuid(), Catalog).Select(p => p.Permission).ToList();
        var withIdentityCatalog = SystemRoleDefinitions.SuperAdmin.Resolve(Guid.NewGuid(), [.. IdentityPermissions.All, .. Catalog])
            .Select(p => p.Permission).Distinct().ToList();

        keys.ShouldBe([PlatformPermissions.TenantsManage]);
        withIdentityCatalog.ShouldBe([PlatformPermissions.TenantsManage]);
        SystemRoleDefinitions.Get(SystemRoleCodes.SuperAdmin).ShouldBeSameAs(SystemRoleDefinitions.SuperAdmin);
    }

    [Theory]
    [MemberData(nameof(RetiredCodes))]
    public void Get_RetiredCode_Throws(string retiredCode)
    {
        Should.Throw<InvalidOperationException>(() => SystemRoleDefinitions.Get(retiredCode));
    }

    public static TheoryData<string> RetiredCodes() => [.. SystemRoleCodes.RetiredSuccessors.Keys];

    private static List<string> Resolve(string roleCode) =>
        SystemRoleDefinitions.Get(roleCode).Resolve(Guid.NewGuid(), Catalog).Select(p => p.Permission).ToList();
}
