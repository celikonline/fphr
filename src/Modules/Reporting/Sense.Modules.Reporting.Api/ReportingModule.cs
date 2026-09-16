using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Infrastructure;
using Sense.Modules.Reporting.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Reporting.Api;

/// <summary>Reporting module composition root: report catalog (contributed by other modules' IReportProvider), on-demand generation, execution history (roadmap #70).</summary>
public sealed class ReportingModule : IModule
{
    public string Name => ReportingDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(ReportingModule).Assembly,
        typeof(ReportingErrors).Assembly,
        typeof(ReportingDbContext).Assembly,
        typeof(ReportingPermissions).Assembly,
    ];

    public IEnumerable<Permission> Permissions => ReportingPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ReportingDbContext>(configuration, ReportingDbContext.SchemaName);

        services.AddModuleHandlers(
            ReportingDbContext.SchemaName,
            typeof(ReportingErrors).Assembly,
            typeof(ReportingDbContext).Assembly,
            typeof(ReportingPermissions).Assembly);

        services.AddReportingCoreServices();
    }
}
