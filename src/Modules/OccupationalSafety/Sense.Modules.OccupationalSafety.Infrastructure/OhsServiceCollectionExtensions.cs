using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.OccupationalSafety.Application;
using Sense.Modules.OccupationalSafety.Domain;
using Sense.Modules.OccupationalSafety.Infrastructure.Persistence;

namespace Sense.Modules.OccupationalSafety.Infrastructure;

public static class OhsServiceCollectionExtensions
{
    /// <summary>
    /// Services the Occupational Safety module needs (repos, unit of work). Used by both the API module
    /// (OhsModule.AddModule) and Sense.Worker (mirrors Assets' AddAssetsCoreServices).
    /// </summary>
    public static IServiceCollection AddOhsCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IWorkplaceHazardClassificationRepository, WorkplaceHazardClassificationRepository>();
        services.AddScoped<IRiskAssessmentRepository, RiskAssessmentRepository>();
        services.AddScoped<IOhsUnitOfWork>(sp => sp.GetRequiredService<OhsDbContext>());
        return services;
    }
}
