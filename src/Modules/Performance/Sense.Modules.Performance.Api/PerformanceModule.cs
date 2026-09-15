using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Modules.Performance.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Performance.Api;

/// <summary>Performance module composition root: review cycles, competencies/questions, participants/assignments, scoring.</summary>
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
        services.AddModuleDbContext<PerformanceDbContext>(configuration, PerformanceDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor,
        // and Contracts events (ReviewCyclePublished/ReviewParticipantCompleted) map to this module's outbox.
        services.AddModuleHandlers(
            PerformanceDbContext.SchemaName,
            typeof(PerformanceErrors).Assembly,
            typeof(PerformanceDbContext).Assembly,
            typeof(IReviewCycleRepository).Assembly,
            typeof(PerformancePermissions).Assembly);

        services.AddScoped<IReviewCycleRepository, ReviewCycleRepository>();
    }
}
