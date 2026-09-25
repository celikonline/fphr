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
    /// <summary>
    /// Kullanıcı grupları: İK Yöneticisi ve Muhasebe şirket içinde her şeyi yapabilir (platform izinleri hariç — o yalnız
    /// <see cref="SuperAdmin"/>'de), Müdür kendi ekibini görür ve ekibinin onaylarını verir, Çalışan yalnız self-servis. Owner, ilk İK Yöneticisinin taşıdığı kilitli bayraktır
    /// (izinleri <see cref="SystemRolePermissionResolver"/> tüm katalogdan verir). Eski roller için bkz.
    /// <see cref="SystemRoleCodes.RetiredSuccessors"/>.
    /// </summary>
    public static readonly IReadOnlyList<SystemRoleDefinition> All =
    [
        new(SystemRoleCodes.Owner, DataScope.Company, [PermissionPatterns.All], []),

        new(SystemRoleCodes.HR, DataScope.Company, [PermissionPatterns.All], []),

        new(SystemRoleCodes.PayrollSpecialist, DataScope.Company, [PermissionPatterns.All], []),

        // Müdür: ekip kapsamı (bağlı çalışanlar). İzin/harcama onayı Onay Kutusu'ndan, aylık puantaj onayı, vardiya
        // planlama, ekip hedefleri, mülakat formu; maaş/hassas veri yok.
        new(SystemRoleCodes.Manager, DataScope.Team,
            ["workflow.inbox.*", "leave.requests.read", "leave.calendar.read", "leave.balances.read", "attendance.*.view",
             "attendance.timesheet.resolve", "attendance.timesheet.approve", "attendance.shift.plan", "attendance.shift.publish",
             "expense.claims.view", "expense.claims.approve", "expense.categories.view", "employee.*.view", "organization.*.view",
             "performance.team.*", "performance.goals.*", "compensation.proposals.*", "training.*.view", "recruitment.interviews.*",
             "reports.team.*", "documents.*.view",
             // Zaman & devam: elle okutma, eksik kayıt talepleri ve çalışan notları (ekip kapsamı).
             "attendance.punch.manage", "attendance.punchrequest.manage", "employee.notes.manage",
             .. PermissionPatterns.SelfService],
            [.. PermissionPatterns.Sensitive]),

        new(SystemRoleCodes.Employee, DataScope.Self,
            [.. PermissionPatterns.SelfService, "org.directory.view", "org.chart.view", "employee.directory.view", "employee.orgchart.view", "organization.holidays.view",
             "announcements.feed.*", "documents.company.view", "ai.assistant.use",
             // QA teslim testi bulgu 12: harcama kalemi eklerken kategori listesi (yalnız okuma).
             "expense.categories.view",
             // Ekip yöneticileri artık Çalışan grubunda: izin/harcama onayı organizasyondaki yöneticiye atanır, bu yüzden
             // Onay Kutusu herkese açık. İkisi de atama bazlıdır (ListInbox yalnız kendi görevleri, Decide AssigneeMismatch).
             "workflow.inbox.view", "workflow.inbox.decide"],
            []),
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
