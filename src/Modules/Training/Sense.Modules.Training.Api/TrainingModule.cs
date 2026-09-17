using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Contracts;
using Sense.Modules.Training.Domain;
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
        typeof(TrainingPermissions).Assembly,
        typeof(TrainingDbContext).Assembly,
    ];

    public IEnumerable<Permission> Permissions => TrainingPermissions.All;

    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<TrainingDbContext>(configuration, TrainingDbContext.SchemaName);

        // Domain + Contracts assemblies too: their domain/integration event types must resolve in OutboxProcessor
        services.AddModuleHandlers(
            TrainingDbContext.SchemaName,
            typeof(TrainingErrors).Assembly,
            typeof(TrainingDbContext).Assembly,
            typeof(TrainingPermissions).Assembly);

        services.AddScoped<ITrainingUnitOfWork>(sp => sp.GetRequiredService<TrainingDbContext>());
    }
}
