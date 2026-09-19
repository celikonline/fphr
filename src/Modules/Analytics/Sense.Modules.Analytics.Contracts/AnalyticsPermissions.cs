using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Analytics.Contracts;

/// <summary>Analytics module permission definitions.</summary>
public static class AnalyticsPermissions
{
    private const string Module = "analytics";
    private const string Group = "Analitik";

    /// <summary>View company-wide workforce KPIs (turnover, career path ratio, gender ratio, etc.).</summary>
    public static readonly Permission KpiView = new(
        "analytics.kpi.view", Module, "İnsan Kaynağı Göstergelerini Görüntüleme", Group, "Şirket geneli insan kaynağı göstergelerini görüntüleme (kariyer yolu oranı, cinsiyet dağılımı vb.)");

    public static IEnumerable<Permission> All => [KpiView];
}
