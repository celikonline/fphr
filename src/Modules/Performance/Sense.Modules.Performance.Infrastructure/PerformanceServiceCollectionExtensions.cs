using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Infrastructure.Persistence;

namespace Sense.Modules.Performance.Infrastructure;

public static class PerformanceServiceCollectionExtensions
{
    /// <summary>Performance's SaveChanges port for its integration event handlers (see <see cref="IPerformanceUnitOfWork"/>).</summary>
    public static IServiceCollection AddPerformanceCoreServices(this IServiceCollection services)
    {
        services.AddScoped<IPerformanceUnitOfWork>(sp => sp.GetRequiredService<PerformanceDbContext>());
        return services;
    }
}
