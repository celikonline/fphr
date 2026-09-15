using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Deliveries;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Notifications.Infrastructure.Delivery;

/// <summary>
/// Sends due Pending e-mail deliveries across all tenants. Rows are claimed with FOR UPDATE SKIP LOCKED inside one
/// transaction (several Worker instances never send the same row), each row is processed and saved inside its own
/// tenant scope (AuditTenantInterceptor rejects cross-tenant writes).
/// </summary>
public sealed partial class EmailDeliveryProcessor(
    NotificationsDbContext db,
    ITenantContextSetter tenantSetter,
    EmailDeliverySender sender,
    IOptions<NotificationsOptions> options,
    TimeProvider clock,
    ILogger<EmailDeliveryProcessor> logger)
{
    public async Task<int> ProcessAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0;
        await db.ExecuteInTransactionAsync(async ct =>
        {
            var sql = string.Format(
                CultureInfo.InvariantCulture, DeliverySql.SelectDue, NotificationsDbContext.SchemaName, options.Value.Delivery.BatchSize);
            var due = await db.Deliveries
                .FromSqlRaw(sql, nameof(NotificationDeliveryStatus.Pending), clock.GetUtcNow().UtcDateTime)
                .IgnoreQueryFilters()
                .ToListAsync(ct);

            foreach (var delivery in due)
            {
                using (tenantSetter.BeginScope(delivery.TenantId))
                using (CurrentUserAccessor.UseSystem())
                {
                    await sender.SendAsync(delivery, ct);
                    await db.SaveChangesAsync(ct);
                }

                if (delivery.Status == NotificationDeliveryStatus.Failed)
                {
                    Log.GaveUp(logger, delivery.Id, delivery.TemplateKey, delivery.Attempts, delivery.LastError);
                }

                processed++;
            }
        }, cancellationToken);

        return processed;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4300, Level = LogLevel.Error, Message = "E-mail delivery {DeliveryId} ({TemplateKey}) failed permanently after {Attempts} attempts: {LastError}")]
        public static partial void GaveUp(ILogger logger, Guid deliveryId, string templateKey, int attempts, string? lastError);
    }
}

internal static class DeliverySql
{
    /// <summary>{0}=schema, {1}=batch size; {{0}}=status, {{1}}=now parameters.</summary>
    public const string SelectDue = """
        SELECT * FROM "{0}"."notification_deliveries"
        WHERE status = {{0}} AND next_attempt_at <= {{1}}
        ORDER BY next_attempt_at
        LIMIT {1}
        FOR UPDATE SKIP LOCKED
        """;
}

/// <summary>
/// Sense.Worker loop around <see cref="EmailDeliveryProcessor"/> (same shape as the Worker's OutboxPollingService). A
/// failing batch (e.g. database unavailable) is logged and retried after the polling interval instead of stopping the host.
/// </summary>
public sealed partial class EmailDeliveryPollingService(
    IServiceProvider services, IOptions<NotificationsOptions> options, ILogger<EmailDeliveryPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.Delivery.PollingIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                using var scope = services.CreateScope();
                processed = await scope.ServiceProvider.GetRequiredService<EmailDeliveryProcessor>().ProcessAsync(stoppingToken);
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
        [LoggerMessage(EventId = 4301, Level = LogLevel.Error, Message = "E-mail delivery batch failed; retrying after the polling interval")]
        public static partial void BatchFailed(ILogger logger, Exception exception);
    }
}
