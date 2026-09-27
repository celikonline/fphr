using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Deliveries;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Notifications.Infrastructure.Delivery;

/// <summary>
/// Sends due Pending push deliveries across all tenants - same claim/transaction shape as
/// <see cref="EmailDeliveryProcessor"/> (one transaction per row, FOR UPDATE SKIP LOCKED via the shared
/// <see cref="DeliverySql.ClaimById"/>), just filtered to <see cref="NotificationChannels.Push"/> and delegating the
/// actual send to <see cref="PushDeliverySender"/>.
/// </summary>
public sealed partial class PushDeliveryProcessor(
    NotificationsDbContext db,
    ITenantContextSetter tenantSetter,
    PushDeliverySender sender,
    IOptions<NotificationsOptions> options,
    TimeProvider clock,
    ILogger<PushDeliveryProcessor> logger)
{
    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var dueIds = await db.Deliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Channel == NotificationChannels.Push && d.Status == NotificationDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id)
            .Take(options.Value.Push.BatchSize)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var id in dueIds)
        {
            if (await ProcessOneAsync(id, cancellationToken))
            {
                processed++;
            }
        }

        return processed;
    }

    private async Task<bool> ProcessOneAsync(Guid id, CancellationToken cancellationToken)
    {
        var handled = false;
        await db.ExecuteInTransactionAsync(async ct =>
        {
            var sql = string.Format(CultureInfo.InvariantCulture, DeliverySql.ClaimById, NotificationsDbContext.SchemaName);
            var claimed = await db.Deliveries
                .FromSqlRaw(sql, id, nameof(NotificationDeliveryStatus.Pending), clock.GetUtcNow().UtcDateTime)
                .IgnoreQueryFilters()
                .ToListAsync(ct);

            // Empty when another worker holds the row, or when it stopped being due since the scan.
            if (claimed is not [var delivery])
            {
                handled = false;
                return;
            }

            using (tenantSetter.BeginScope(delivery.TenantId))
            using (CurrentUserAccessor.UseSystem())
            {
                await sender.SendAsync(delivery, ct);
                await db.SaveChangesAsync(ct);
            }

            handled = true;

            if (delivery.Status == NotificationDeliveryStatus.Failed)
            {
                Log.GaveUp(logger, delivery.Id, delivery.TemplateKey, delivery.Attempts, delivery.LastError);
            }
        }, cancellationToken);

        return handled;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4310, Level = LogLevel.Error, Message = "Push delivery {DeliveryId} ({TemplateKey}) failed permanently after {Attempts} attempts: {LastError}")]
        public static partial void GaveUp(ILogger logger, Guid deliveryId, string templateKey, int attempts, string? lastError);
    }
}

/// <summary>Sense.Worker loop around <see cref="PushDeliveryProcessor"/> (same shape as <see cref="EmailDeliveryPollingService"/>).</summary>
public sealed partial class PushDeliveryPollingService(
    IServiceProvider services, IOptions<NotificationsOptions> options, ILogger<PushDeliveryPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.Push.PollingIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                using var scope = services.CreateScope();
                processed = await scope.ServiceProvider.GetRequiredService<PushDeliveryProcessor>().ProcessAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.BatchFailed(logger, ex);
            }

            if (processed == 0)
            {
                await Task.Delay(interval, stoppingToken);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4311, Level = LogLevel.Error, Message = "Push delivery batch failed; retrying after the polling interval")]
        public static partial void BatchFailed(ILogger logger, Exception exception);
    }
}
