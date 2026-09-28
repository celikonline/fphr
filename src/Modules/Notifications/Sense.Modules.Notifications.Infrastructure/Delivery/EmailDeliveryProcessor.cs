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
/// Sends due Pending e-mail deliveries across all tenants. Each row gets its own transaction: it is claimed with
/// FOR UPDATE SKIP LOCKED (several Worker instances never send the same row), sent, and saved inside its own tenant
/// scope (AuditTenantInterceptor rejects cross-tenant writes).
/// </summary>
/// <remarks>
/// One transaction per row, not per batch: an SMTP send cannot be rolled back, so a transaction spanning several sends
/// turns one rollback (worker shutdown, transient database error - note the retrying execution strategy replays the
/// whole delegate) into re-sent mail for every row it had already delivered.
/// </remarks>
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
        var now = clock.GetUtcNow().UtcDateTime;

        // Unlocked scan: the claim below re-checks the row, so a racing worker only costs us a skipped id.
        var dueIds = await db.Deliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.Channel == NotificationChannels.Email && d.Status == NotificationDeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).ThenBy(d => d.Id)
            .Take(options.Value.Delivery.BatchSize)
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
        [LoggerMessage(EventId = 4300, Level = LogLevel.Error, Message = "E-mail delivery {DeliveryId} ({TemplateKey}) failed permanently after {Attempts} attempts: {LastError}")]
        public static partial void GaveUp(ILogger logger, Guid deliveryId, string templateKey, int attempts, string? lastError);
    }
}

internal static class DeliverySql
{
    /// <summary>{0}=schema; {{0}}=id, {{1}}=status, {{2}}=now parameters. Empty when another worker holds the row.</summary>
    public const string ClaimById = """
        SELECT * FROM "{0}"."notification_deliveries"
        WHERE id = {{0}} AND status = {{1}} AND next_attempt_at <= {{2}}
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
