using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.BackgroundJobs;

namespace Sense.Shared.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Hangfire background job services.
/// </summary>
public static class BackgroundJobServiceCollectionExtensions
{
    private const int DefaultWorkerCount = 2;
    private const int PollingIntervalSeconds = 15;

    /// <summary>
    /// Registers Hangfire with PostgreSQL storage and background job services.
    /// </summary>
    public static IServiceCollection AddHangfireServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringNames.Database);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is required for Hangfire configuration.");
        }

        // Configure Hangfire with PostgreSQL storage
        services.AddHangfire((sp, config) =>
        {
            config.UsePostgreSqlStorage(
                opts => opts.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    QueuePollInterval = TimeSpan.FromSeconds(PollingIntervalSeconds),
                    PrepareSchemaIfNecessary = true,
                    DistributedLockTimeout = TimeSpan.FromSeconds(30),
                    UseNativeDatabaseTransactions = true,
                });

            config.UseRecommendedSerializerSettings();
        });

        // Register Hangfire server (enables background processing)
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = DefaultWorkerCount;
            options.StopTimeout = TimeSpan.FromSeconds(30);
            options.SchedulePollingInterval = TimeSpan.FromSeconds(PollingIntervalSeconds);
        });

        // Register background job services
        services.AddScoped<IBackgroundJobService, BackgroundJobService>();
        services.AddScoped<IBackgroundJobExecutor, BackgroundJobExecutor>();

        return services;
    }
}
