using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.OccupationalSafety.Application;
using Sense.Modules.OccupationalSafety.Contracts;
using Sense.Modules.OccupationalSafety.Infrastructure;
using Sense.Modules.OccupationalSafety.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.OccupationalSafety.Api;

/// <summary>
/// Occupational Safety (İSG, senseik-015) module composition root. Phase 0-1 scope: workplace hazard
/// classification + risk assessments only - committee/accident/PPE/document-engine integration are separate,
/// later phases (see docs/handoffs/occupational-safety-module.md).
/// </summary>
public sealed class OhsModule : IModule
{
    public string Name => OhsDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(OhsModule).Assembly,
        typeof(OhsPermissions).Assembly,
        typeof(OhsDbContext).Assembly,
        typeof(IOhsUnitOfWork).Assembly,
    ];

    public IEnumerable<Permission> Permissions => OhsPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates - same wiring as LeaveModule/ExpenseModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<OhsDbContext>(
            configuration, OhsDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<OhsDbContext>>();

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see AssetsModule.AddModule's identical comment).
        services.AddModuleHandlers(
            OhsDbContext.SchemaName,
            typeof(IOhsUnitOfWork).Assembly,
            typeof(OhsDbContext).Assembly,
            typeof(OhsPermissions).Assembly);

        services.AddOhsCoreServices();
    }
}
