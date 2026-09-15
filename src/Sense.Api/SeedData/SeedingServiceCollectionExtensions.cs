using Microsoft.Extensions.DependencyInjection;
using Sense.Api.SeedData;

namespace Sense.Shared.Infrastructure.DependencyInjection;

/// <summary>Extension methods for registering seed data services.</summary>
public static class SeedingServiceCollectionExtensions
{
    /// <summary>Register seed data seeder service.</summary>
    public static IServiceCollection AddDataSeeding(this IServiceCollection services)
    {
        services.AddScoped<DataSeeder>();
        return services;
    }
}
