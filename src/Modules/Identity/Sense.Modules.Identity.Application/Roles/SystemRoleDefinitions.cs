using System.Text.RegularExpressions;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application.Roles;

/// <summary>
/// Varsayılan kullanıcı tipleri: kapsam ve izin kuralları. Görünen ad/açıklama kaynak anahtarındadır (roles.{code}.name).
/// İzinler tam anahtar veya joker desenle (`*.view`, `leave.*`) tanımlanır; seed sırasında tüm modül kataloğuna uygulanır.
/// Modül eklendikçe roller otomatik genişler; Owner her zaman tüm izinleri alır.
/// </summary>
public sealed record SystemRoleDefinition(string Code, DataScope Scope, IReadOnlyList<string> Include, IReadOnlyList<string> Exclude)
{
    public string NameKey => SystemRoleCodes.NameKey(Code);

    public string DescriptionKey => SystemRoleCodes.DescriptionKey(Code);

    public IEnumerable<RolePermission> Resolve(Guid roleId, IReadOnlyList<Permission> catalog)
    {
        var include = Include.Select(PermissionPatterns.ToRegex).ToList();
        var exclude = Exclude.Select(PermissionPatterns.ToRegex).ToList();

        // Platform anahtarları ("platform.*") yalnız "platform." ile başlayan AÇIK bir desenle verilir; "*", "*.manage",
        // "identity.*" gibi genel jokerler onları asla eşlemez (Owner/Admin/ITAdmin/Auditor süper admin yetkisi almasın).
        var platformInclude = Include
            .Where(pattern => pattern.StartsWith(PlatformPermissions.Prefix, StringComparison.Ordinal))
            .Select(PermissionPatterns.ToRegex)
            .ToList();

        foreach (var p in catalog)
        {
            var included = PlatformPermissions.IsPlatformKey(p.Key)
                ? platformInclude.Any(r => r.IsMatch(p.Key))
                : include.Any(r => r.IsMatch(p.Key));

            if (included && !exclude.Any(r => r.IsMatch(p.Key)))
            {
                yield return new RolePermission(roleId, p.Key, (int)Scope);
            }
        }
    }
}

/// <summary>
/// Sistem rolü → izin listesi çözümlemesi (seed ve senkronizasyon için tek kaynak). Kilitli Owner rolü tüm kataloğu alır,
/// ANCAK platform izinleri hariç (<see cref="PlatformPermissions"/>).
/// </summary>
public static class SystemRolePermissionResolver
{
    public static List<RolePermission> Resolve(Role role, IReadOnlyList<Permission> catalog) => role.IsLocked
        ? catalog.Where(p => !PlatformPermissions.IsPlatformKey(p.Key)).Select(p => new RolePermission(role.Id, p.Key, (int)DataScope.Company)).ToList()
        : SystemRoleDefinitions.Get(role.Code).Resolve(role.Id, catalog).ToList();
}

/// <summary>Joker desen sözdizimi: '*' = herhangi bir karakter dizisi.</summary>
public static class PermissionPatterns
{
    public const string Wildcard = "*";
    public const string All = "*";
    private const string WildcardRegex = ".*";
    private const string Anchor = "^";
    private const string End = "$";

    public static Regex ToRegex(string pattern) =>
        new(Anchor + Regex.Escape(pattern).Replace(Regex.Escape(Wildcard), WildcardRegex, StringComparison.Ordinal) + End,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly string[] ReadOnly = ["*.view", "*.view_all", "*.read", "*.export", "*.list", "*.report", "*.reports"];
    public static readonly string[] Sensitive = ["*.salary.*", "*.sensitive.*", "*.sensitive_view", "identity.audit.sensitive_view"];
    public static readonly string[] SelfService = ["*.self.*", "*.my.*", "me.*"];
    public static readonly string[] Writes = ["*.manage", "*.create", "*.update", "*.delete", "*.approve", "*.lock", "*.publish", "*.submit"];
}

public static class SystemRoleDefinitions
{
    public static readonly IReadOnlyList<SystemRoleDefinition> All =
    [
        new(SystemRoleCodes.Owner, DataScope.Company, [PermissionPatterns.All], []),

        // Faz 8 #103 ("Ayarlar > Ödeme bilgileri" / billing): the source transcript has admins reaching billing
        // while owners cannot, the opposite of Owner-only billing access. Decision (11.3.01-kimlik-ve-erisim.md #8):
        // keep Owner's billing.* access, but stop excluding it from Admin too — both types get it now.
        new(SystemRoleCodes.Admin, DataScope.Company, [PermissionPatterns.All], []),

        new(SystemRoleCodes.HR, DataScope.Company,
            ["identity.users.*", "identity.roles.view", "identity.audit.view", "organization.*", "employee.*", "leave.*", "workflow.*", "attendance.*",
             "assets.*", "expense.*", "training.*", "documents.*", "announcements.*", "performance.*", "compensation.*", "recruitment.*",
             "analytics.*", "reports.*", "notifications.*", "payroll.payslips.view", "payroll.periods.view", "ai.*", "datatransfer.*", AccessSettingPermissions.Bypass,
             .. PermissionPatterns.SelfService,
             // senseik-015 (İSG modülü, Faz 0-1): hazard classification + risk assessments are HR/safety-compliance
             // territory, same tier as assets.*/documents.* above - appended rather than inserted to keep this a
             // pure addition (hot file, append-only).
             "ohs.*"],
            [// senseik-050: kimlik yönetimi yetkileri (kullanıcı oluşturma/düzenleme + rol atama, 2FA sıfırlama,
             // askıya alma/etkinleştirme) yalnız System Administrator'da (Owner/Admin, joker "*" ile) kalmalı -
             // "identity.users.*" include joker'inden açıkça hariç tutuldu (bu dosyada zaten kullanılan desen,
             // bkz. Timekeeper'ın "attendance.*"den hariç tuttuğu anahtarlar).
             "identity.users.manage", "identity.users.suspend", "identity.users.reset_mfa",
             "identity.users.impersonate", "identity.security.manage", "identity.api_keys.manage", "payroll.periods.lock", "billing.*"]),

        new(SystemRoleCodes.HRSpecialist, DataScope.Company,
            ["organization.*.view", "employee.*", "leave.*", "workflow.*", "attendance.*", "assets.*", "expense.*.view", "training.*", "documents.*",
             "announcements.*", "recruitment.*", "reports.*", "datatransfer.*",
             // G-10-18: HR role (line above) already had analytics.* - HRSpecialist (the day-to-day HR persona) had
             // reports.* (Reporting catalog) but not this, so the workforce KPI dashboard (/app/reports "İK
             // göstergeleri") was invisible to them despite being exactly who'd use it.
             "analytics.*",
             .. PermissionPatterns.SelfService,
             // senseik-015 (İSG modülü, Faz 0-1): same reasoning as the HR role above.
             "ohs.*"],
            [.. PermissionPatterns.Sensitive, "employee.terminate", "compensation.*", "payroll.*", "employee.*.delete"]),

        new(SystemRoleCodes.PayrollSpecialist, DataScope.Company,
            ["payroll.*", "attendance.timesheets.*", "attendance.*.view", "leave.*.view", "employee.*.view", "employee.salary.*", "employee.bank_accounts.*", "expense.adjustments.*",
             "expense.bank_payments.*", "reports.*", "organization.*.view", "documents.*.view", .. PermissionPatterns.SelfService, "leave.*.read",
             // QA teslim testi bulgu 12: expense.my.claims sahipleri kalem eklerken kategori listesini okuyabilmeli.
             "expense.categories.view"],
            []),

        new(SystemRoleCodes.Finance, DataScope.Company,
            ["expense.*", "compensation.budgets.view", "billing.*", "reports.expense.*", "reports.*.view", "employee.*.view", "organization.*.view", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Manager, DataScope.Team,
            ["leave.requests.view", "leave.requests.approve", "leave.calendar.view", "leave.balances.view", "workflow.inbox.*", "attendance.*.view",
             "attendance.corrections.approve", "expense.claims.view", "expense.claims.approve", "employee.*.view", "organization.*.view", "performance.team.*",
             "performance.goals.*", "compensation.proposals.*", "training.*.view", "recruitment.interviews.*", "reports.team.*", "documents.*.view",
             .. PermissionPatterns.SelfService, "leave.balances.read", "leave.calendar.read",
             // Ana Sayfa "İzin Talepleri" widget needs the team's pending leave requests; "leave.requests.view"/".approve"
             // above are stale placeholders that never matched a real LeavePermissions key (same pattern as the two keys
             // above this comment) - appended the real key rather than replacing them.
             "leave.requests.read",
             // Package A3: 11.3.06 spec names the resolve permission "attendance.timesheet.resolve" (matching the
             // module's other attendance.timesheet.* keys), not the "attendance.corrections.approve" placeholder
             // above that was never wired to an actual permission - appended rather than replacing it (hot file).
             "attendance.timesheet.resolve",
             // Package A4: role mapping "Manager → view/resolve/plan/publish @Team" - Manager plans and publishes
             // shifts for their team but does not get shift.settings/.lock/.unlock (Owner/Admin/HR only, via attendance.*).
             "attendance.shift.plan", "attendance.shift.publish",
             // Package A5: "Aylık Puantaj" onayı (S-11) - Manager approves their own team's monthly puantaj, same
             // shape as attendance.timesheet.resolve above.
             "attendance.timesheet.approve",
             // QA teslim testi bulgu 12: kendi harcama talebine kalem eklerken kategori listesi (yalnız okuma).
             "expense.categories.view"],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Timekeeper, DataScope.Department,
            ["attendance.*", "leave.*.view", "employee.*.view", "organization.*.view", "reports.attendance.*", .. PermissionPatterns.SelfService, "leave.*.read"],
            ["attendance.timesheets.lock", "attendance.timesheets.unlock", "attendance.settings.*",
             // Package A4: role mapping "Timekeeper → view/edit/resolve/import/plan/publish/availability.view @Department|Location
             // (approve/lock yok)" - the excludes above never matched this module's actual key shape (attendance.shift.*,
             // not attendance.timesheets.*/attendance.settings.*), so Timekeeper's "attendance.*" wildcard would otherwise
             // have granted shift lock/unlock/settings too; excluded explicitly instead.
             "attendance.shift.lock", "attendance.shift.unlock", "attendance.shift.settings",
             // senseik-056: same role mapping says "(approve ... yok)" - the monthly puantaj approval (Onayla / Tümünü Onayla)
             // is HR/Manager territory, so the attendance.* wildcard must not hand it to Timekeeper.
             "attendance.timesheet.approve", .. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Recruiter, DataScope.Company,
            ["recruitment.*", "employee.create", "employee.*.view", "organization.*.view", "reports.recruitment.*", "documents.*.view", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Interviewer, DataScope.Custom,
            ["recruitment.interviews.view", "recruitment.interviews.notes", "recruitment.form_templates.read", "recruitment.form_responses.read",
             "recruitment.form_responses.submit", "recruitment.candidates.summary", .. PermissionPatterns.SelfService],
            []),

        new(SystemRoleCodes.Auditor, DataScope.Company,
            ["identity.audit.view", "identity.users.view", "identity.roles.view", "identity.tenant.view", .. PermissionPatterns.ReadOnly, "reports.*",
             "documents.consents.*.view", "integrations.*.view", "integrations.webhooks.deliveries", "payroll.trace.view", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive, .. PermissionPatterns.Writes]),

        new(SystemRoleCodes.ExternalAccountant, DataScope.Company,
            ["payroll.*.view", "payroll.*.export", "payroll.declarations.*", "payroll.trace.view", "attendance.timesheets.view", "attendance.timesheets.export",
             "employee.*.view", "organization.*.view", "reports.payroll.*", "reports.attendance.*", "expense.bank_payments.view"],
            [.. PermissionPatterns.Sensitive, .. PermissionPatterns.Writes]),

        new(SystemRoleCodes.ITAdmin, DataScope.Company,
            ["identity.*", "integrations.*", "attendance.devices.*", "attendance.checkpoints.*", "notifications.settings.*", .. PermissionPatterns.SelfService],
            ["identity.users.impersonate", "identity.audit.sensitive_view", AccessSettingPermissions.Bypass]),

        new(SystemRoleCodes.Employee, DataScope.Self,
            [.. PermissionPatterns.SelfService, "org.directory.view", "org.chart.view", "employee.directory.view", "employee.orgchart.view", "organization.holidays.view",
             "announcements.feed.*", "documents.company.view", "ai.assistant.use",
             // QA teslim testi bulgu 12: harcama kalemi eklerken kategori listesi (yalnız okuma).
             "expense.categories.view"],
            []),

        new(SystemRoleCodes.Intern, DataScope.Self,
            [.. PermissionPatterns.SelfService, "org.directory.view", "employee.directory.view", "employee.orgchart.view", "organization.holidays.view",
             "announcements.feed.*", "documents.company.view", "expense.categories.view"],
            ["payroll.my.*", "expense.my.advances.*", "ai.*"]),

        new(SystemRoleCodes.Contractor, DataScope.Self,
            ["employee.my.*", "attendance.my.*", "announcements.feed.*", "documents.my.*", "notifications.my.*"],
            []),

        new(SystemRoleCodes.ApiIntegration, DataScope.Company,
            ["*.view", "*.read", "attendance.events.create", "employee.create", "employee.update", "leave.requests.create", "recruitment.applications.create"],
            [.. PermissionPatterns.Sensitive]),
    ];

    /// <summary>
    /// Süper admin: yalnız platform izinleri. <see cref="All"/>'a KASITLI olarak dahil değil — <see cref="All"/> her müşteri
    /// kiracısına seed edilir; bu rol yalnız platform kiracısında oluşturulur (<see cref="SystemRoleCodes.ForTenant"/>).
    /// </summary>
    public static readonly SystemRoleDefinition SuperAdmin =
        new(SystemRoleCodes.SuperAdmin, DataScope.Company, [PlatformPermissions.Wildcard], []);

    public static SystemRoleDefinition Get(string code) =>
        All.Append(SuperAdmin).Single(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
}
