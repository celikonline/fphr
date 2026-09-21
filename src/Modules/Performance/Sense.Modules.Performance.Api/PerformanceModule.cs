using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Infrastructure;
using Sense.Modules.Performance.Infrastructure.Persistence;
using Sense.Shared.Contracts.Audit;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;
using Sense.Shared.Infrastructure.Persistence.Audit;

namespace Sense.Modules.Performance.Api;

/// <summary>Performance (Performans/Hedef/OKR) module composition root. R0: review cycles, review forms, goals - see faz-08 rapor.</summary>
public sealed class PerformanceModule : IModule
{
    public string Name => PerformanceDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(PerformanceModule).Assembly,
        typeof(PerformancePermissions).Assembly,
        typeof(PerformanceDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => PerformancePermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Audit trail for IAuditLogged aggregates - same wiring as LeaveModule/ExpenseModule.
        services.AddScoped<AuditLogInterceptor>();
        services.AddModuleDbContext<PerformanceDbContext>(
            configuration, PerformanceDbContext.SchemaName,
            (sp, options) => options.AddInterceptors(sp.GetRequiredService<AuditLogInterceptor>()));
        services.AddScoped<IAuditLogSource, ModuleAuditLogSource<PerformanceDbContext>>();

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        // (see CLAUDE.md's AddModuleHandlers note - Contracts-assembly-only registration silently registers nothing).
        services.AddModuleHandlers(
            PerformanceDbContext.SchemaName,
            typeof(PerformanceErrors).Assembly,
            typeof(PerformanceDbContext).Assembly,
            typeof(IGoalRepository).Assembly,
            typeof(PerformancePermissions).Assembly);

        services.AddScoped<IReviewCycleRepository, ReviewCycleRepository>();
        services.AddScoped<IReviewFormRepository, ReviewFormRepository>();
        services.AddScoped<IGoalRepository, GoalRepository>();
        // EmployeeTerminatedHandler (Scrutor-scanned above) needs IPerformanceUnitOfWork even though only Sense.Worker
        // ever dispatches it - registered here too so DI validation passes at Api startup.
        services.AddPerformanceCoreServices();
    }
}
