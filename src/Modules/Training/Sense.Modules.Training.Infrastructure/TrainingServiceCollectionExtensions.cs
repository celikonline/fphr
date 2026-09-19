using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Training.Application;
using Sense.Modules.Training.Infrastructure.Persistence;

namespace Sense.Modules.Training.Infrastructure;

public static class TrainingServiceCollectionExtensions
{
    /// <summary>
    /// Services Training needs (repos, unit of work). Used by both the API module (TrainingModule.AddModule) and
    /// Sense.Worker (so the outbox poller resolves), mirroring Assets'/Expense's AddXxxCoreServices.
    /// </summary>
    public static IServiceCollection AddTrainingCoreServices(this IServiceCollection services)
    {
        services.AddScoped<ITrainingUnitOfWork>(sp => sp.GetRequiredService<TrainingDbContext>());
        services.AddScoped<ITrainingProgramRepository, TrainingProgramRepository>();
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<ITrainingRequirementRepository, TrainingRequirementRepository>();
        return services;
    }
}
