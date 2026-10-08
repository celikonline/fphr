using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Reporting.Contracts;

/// <summary>
/// Reporting module permissions. Keys use the "reports." prefix (not "reporting.") to match the wildcards every
/// system role already carries for this not-yet-built module - see <c>SystemRoleDefinitions.cs</c>: Owner/HRAdmin/
/// HRSpecialist/PayrollSpecialist/Auditor already have plain "reports.*" (covers everything below with zero role
/// changes needed), Manager/SelfService already have "*.my.*" (covers <see cref="MyExecutions"/>), Finance already
/// has "reports.*.view" (covers <see cref="CatalogView"/>).
/// </summary>
public static class ReportingPermissions
{
    private const string Module = "reports";

    /// <summary>See the catalog of available reports.</summary>
    public static readonly Permission CatalogView = new(
        "reports.catalog.view",
        Module,
        "Rapor Kataloğunu Görüntüleme",
        "Raporlar",
        "Kullanılabilir raporların listesini görüntüleme");

    /// <summary>Request/run a report (subject to each report's own additional permission, if any).</summary>
    public static readonly Permission ExecutionsGenerate = new(
        "reports.executions.generate",
        Module,
        "Rapor Oluşturma",
        "Raporlar",
        "Bir raporun oluşturulmasını talep etme");

    /// <summary>Self-service: view/download the caller's own report execution history.</summary>
    public static readonly Permission MyExecutions = new(
        "reports.my.executions",
        Module,
        "Kendi Rapor Çalıştırmalarım",
        "Raporlar",
        "Kendi oluşturduğunuz raporları görüntüleme ve indirme");

    /// <summary>Build one-time custom reports from the caller's authorized field catalog.</summary>
    public static readonly Permission CustomBuild = new(
        "reports.custom.build",
        Module,
        "Özel Rapor Oluşturma",
        "Raporlar",
        "İzin verilen alanlardan tek seferlik özel rapor oluşturma");

    public static IEnumerable<Permission> All =>
    [
        CatalogView,
        ExecutionsGenerate,
        MyExecutions,
        CustomBuild,
    ];
}
