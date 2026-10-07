using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Reporting.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Infrastructure.Persistence.Outbox;

namespace Sense.Modules.Reporting.Infrastructure.Jobs;

/// <summary>Reporting's outbox lives on the API host, which owns its Hangfire server and providers.</summary>
public sealed class ReportOutboxHostedService(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, ILogger<ReportOutboxHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor<ReportingDbContext>>().ProcessAsync(stoppingToken);
                if (processed > 0)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reporting outbox polling failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}
