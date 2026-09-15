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
        foreach (var p in catalog)
        {
            if (include.Any(r => r.IsMatch(p.Key)) && !exclude.Any(r => r.IsMatch(p.Key)))
            {
                yield return new RolePermission(roleId, p.Key, (int)Scope);
            }
        }
    }
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

        new(SystemRoleCodes.Admin, DataScope.Company, [PermissionPatterns.All], ["billing.*"]),

        new(SystemRoleCodes.HR, DataScope.Company,
            ["identity.users.*", "identity.roles.view", "identity.audit.view", "organization.*", "employee.*", "leave.*", "workflow.*", "attendance.*",
             "assets.*", "expense.*", "training.*", "documents.*", "announcements.*", "performance.*", "compensation.*", "recruitment.*",
             "analytics.*", "reports.*", "notifications.*", "payroll.payslips.view", "payroll.periods.view", "ai.*", .. PermissionPatterns.SelfService],
            ["identity.users.impersonate", "identity.security.manage", "identity.api_keys.manage", "payroll.periods.lock", "billing.*"]),

        new(SystemRoleCodes.HRSpecialist, DataScope.Company,
            ["organization.*.view", "employee.*", "leave.*", "workflow.*", "attendance.*", "assets.*", "expense.*.view", "training.*", "documents.*",
             "announcements.*", "recruitment.*", "reports.*", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive, "employee.terminate", "compensation.*", "payroll.*", "employee.*.delete"]),

        new(SystemRoleCodes.PayrollSpecialist, DataScope.Company,
            ["payroll.*", "attendance.timesheets.*", "attendance.*.view", "leave.*.view", "employee.*.view", "employee.salary.*", "expense.adjustments.*",
             "expense.bank_payments.*", "reports.*", "organization.*.view", "documents.*.view", .. PermissionPatterns.SelfService],
            []),

        new(SystemRoleCodes.Finance, DataScope.Company,
            ["expense.*", "compensation.budgets.view", "billing.*", "reports.expense.*", "reports.*.view", "employee.*.view", "organization.*.view", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Manager, DataScope.Team,
            ["leave.requests.view", "leave.requests.approve", "leave.calendar.view", "leave.balances.view", "workflow.inbox.*", "attendance.*.view",
             "attendance.corrections.approve", "expense.claims.view", "expense.claims.approve", "employee.*.view", "organization.*.view", "performance.team.*",
             "performance.goals.*", "compensation.proposals.*", "training.*.view", "recruitment.interviews.*", "reports.team.*", "documents.*.view",
             .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Timekeeper, DataScope.Department,
            ["attendance.*", "leave.*.view", "employee.*.view", "organization.*.view", "reports.attendance.*", .. PermissionPatterns.SelfService],
            ["attendance.timesheets.lock", "attendance.timesheets.unlock", "attendance.settings.*", .. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Recruiter, DataScope.Company,
            ["recruitment.*", "employee.create", "employee.*.view", "organization.*.view", "reports.recruitment.*", "documents.*.view", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Interviewer, DataScope.Custom,
            ["recruitment.interviews.view", "recruitment.interviews.notes", "recruitment.scorecards.submit", "recruitment.candidates.summary", .. PermissionPatterns.SelfService],
            []),

        new(SystemRoleCodes.Auditor, DataScope.Company,
            ["identity.audit.view", "identity.users.view", "identity.roles.view", "identity.tenant.view", .. PermissionPatterns.ReadOnly, "reports.*",
             "documents.consents.*.view", "integrations.*.view", "integrations.webhooks.deliveries", .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive, .. PermissionPatterns.Writes]),

        new(SystemRoleCodes.ExternalAccountant, DataScope.Company,
            ["payroll.*.view", "payroll.*.export", "payroll.declarations.*", "attendance.timesheets.view", "attendance.timesheets.export",
             "employee.*.view", "organization.*.view", "reports.payroll.*", "reports.attendance.*", "expense.bank_payments.view"],
            [.. PermissionPatterns.Sensitive, .. PermissionPatterns.Writes]),

        new(SystemRoleCodes.ITAdmin, DataScope.Company,
            ["identity.*", "integrations.*", "attendance.devices.*", "attendance.checkpoints.*", "notifications.settings.*", .. PermissionPatterns.SelfService],
            ["identity.users.impersonate", "identity.audit.sensitive_view"]),

        new(SystemRoleCodes.Employee, DataScope.Self,
            [.. PermissionPatterns.SelfService, "org.directory.view", "org.chart.view", "announcements.feed.*", "documents.company.view", "ai.assistant.use"],
            []),

        new(SystemRoleCodes.Intern, DataScope.Self,
            [.. PermissionPatterns.SelfService, "org.directory.view", "announcements.feed.*", "documents.company.view"],
            ["payroll.my.*", "expense.my.advances.*", "ai.*"]),

        new(SystemRoleCodes.Contractor, DataScope.Self,
            ["employee.my.*", "attendance.my.*", "announcements.feed.*", "documents.my.*"],
            []),

        new(SystemRoleCodes.ApiIntegration, DataScope.Company,
            ["*.view", "*.read", "attendance.events.create", "employee.create", "employee.update", "leave.requests.create", "recruitment.applications.create"],
            [.. PermissionPatterns.Sensitive]),
    ];

    public static SystemRoleDefinition Get(string code) => All.Single(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
}
