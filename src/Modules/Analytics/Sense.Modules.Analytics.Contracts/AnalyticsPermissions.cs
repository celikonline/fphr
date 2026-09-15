using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Analytics.Contracts;

/// <summary>Analytics module permission definitions.</summary>
public static class AnalyticsPermissions
{
    private const string Module = "analytics";
    private const string Group = "Analytics";

    /// <summary>View company-wide workforce KPIs (turnover, career path ratio, gender ratio, etc.).</summary>
    public static readonly Permission KpiView = new(
        "analytics.kpi.view", Module, "View Workforce KPIs", Group, "View company-wide workforce KPIs (career path ratio, gender ratio, etc.)");

    public static IEnumerable<Permission> All => [KpiView];
}
