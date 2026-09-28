using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Application.Import;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Infrastructure;
using Sense.Modules.Assets.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Assets.Api;

/// <summary>Assets module composition root: company equipment ("zimmet") assigned to employees - categories, assign/return/retire.</summary>
public sealed class AssetsModule : IModule
{
    public string Name => AssetsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(AssetsModule).Assembly,
        typeof(AssetsErrors).Assembly,
        typeof(AssetsDbContext).Assembly,
        typeof(AssetsPermissions).Assembly,
    ];

    public IEnumerable<Permission> Permissions => AssetsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates - same wiring as LeaveModule/ExpenseModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<AssetsDbContext>(
            configuration, AssetsDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<AssetsDbContext>>();

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see ExpenseModule.AddModule's identical comment).
        services.AddModuleHandlers(
            AssetsDbContext.SchemaName,
            typeof(AssetsErrors).Assembly,
            typeof(AssetsDbContext).Assembly,
            typeof(AssetsPermissions).Assembly);

        services.AddAssetsCoreServices();

        services.AddScoped<IImportTemplate, AssetImportTemplate>();
        services.AddScoped<IImportRowHandler, AssetImportRowHandler>();
    }
}
