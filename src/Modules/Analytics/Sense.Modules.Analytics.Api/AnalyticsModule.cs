using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Analytics.Contracts;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Analytics.Api;

/// <summary>
/// Analytics module composition root: company-wide workforce KPIs (roadmap Faz 7). No persisted state yet - KPIs
/// are computed on demand from other modules' Contracts; add a DbContext when a KPI needs caching or a snapshot
/// history.
/// </summary>
public sealed class AnalyticsModule : IModule
{
    public string Name => "analytics";

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(AnalyticsModule).Assembly,
        typeof(AnalyticsPermissions).Assembly,
        typeof(GetWorkforceKpisQuery).Assembly,
    ];

    public IEnumerable<Permission> Permissions => AnalyticsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration) =>
        services.AddModuleHandlers(Name, typeof(AnalyticsPermissions).Assembly, typeof(GetWorkforceKpisQuery).Assembly);
}
