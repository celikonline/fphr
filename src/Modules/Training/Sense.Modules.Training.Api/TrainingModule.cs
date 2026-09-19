using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Contracts;
using Sense.Modules.Training.Domain;
using Sense.Modules.Training.Infrastructure;
using Sense.Modules.Training.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.DependencyInjection;

namespace Sense.Modules.Training.Api;

/// <summary>Training (Eğitim) module composition root.</summary>
public sealed class TrainingModule : IModule
{
    public string Name => TrainingDbContext.SchemaName;

    public IReadOnlyList<Assembly> Assemblies { get; } =
    [
        typeof(TrainingModule).Assembly,
        typeof(TrainingErrors).Assembly,
        typeof(TrainingDbContext).Assembly,
        typeof(TrainingPermissions).Assembly,
    ];

    public IEnumerable<Permission> Permissions => TrainingPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TrainingDbContext>(configuration, TrainingDbContext.SchemaName);

        // Domain + Application + Infrastructure + Contracts assemblies: handlers/commands/queries + domain/integration
        // event types must resolve in OutboxProcessor (CLAUDE.md: Contracts-only registration silently registers nothing).
        services.AddModuleHandlers(
            TrainingDbContext.SchemaName,
            typeof(TrainingProgram).Assembly, // Domain
            typeof(ITrainingProgramRepository).Assembly, // Application
            typeof(TrainingDbContext).Assembly, // Infrastructure
            typeof(TrainingPermissions).Assembly); // Contracts

        services.AddTrainingCoreServices();
    }
}
