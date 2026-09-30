using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Announcements.Infrastructure.Jobs;

/// <summary>
/// PublishScheduledAnnouncements job (TK-016, roadmap 11.8 "Duyurular: zamanlı yayın"): a draft announcement can be
/// created with an optional future <see cref="Announcement.ScheduledFor"/>; this job polls for announcements whose
/// schedule has come due (<c>Status == Scheduled &amp;&amp; ScheduledFor &lt;= now</c>) and publishes them, tenant by
/// tenant (each tenant in its own tenant scope, DI scope and transaction) - mirrors the existing Sense.Worker job
/// pattern (Leave module's LeaveBalanceRecalculationJob/LeaveBalanceRecalculationHostedService; the card originally
/// asked for a Hangfire recurring job, but this codebase has no Hangfire, so it follows the BackgroundService pattern
/// already used across the Worker instead).
/// Announcements created without a ScheduledFor are unaffected: they stay Draft until a human calls Publish, exactly
/// as before this job existed.
/// </summary>
public sealed partial class PublishScheduledAnnouncementsJob(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<PublishScheduledAnnouncementsJob> logger)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        List<Guid> tenantIds;
        using (var scope = scopes.CreateScope())
        {
            tenantIds = await scope.ServiceProvider.GetRequiredService<AnnouncementsDbContext>().Announcements
                .IgnoreQueryFilters([ModuleDbContext.TenantFilter])
                .Where(a => a.Status == AnnouncementStatus.Scheduled && a.ScheduledFor != null && a.ScheduledFor <= now)
                .Select(a => a.TenantId).Distinct().ToListAsync(cancellationToken);
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            try
            {
                total += await PublishDueForTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenantId);
            }
        }

        LogCompleted(logger, total, tenantIds.Count);
        return total;
    }

    private async Task<int> PublishDueForTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        using var tenant = scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().BeginScope(tenantId);
        var announcements = scope.ServiceProvider.GetRequiredService<IAnnouncementRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IAnnouncementsUnitOfWork>();

        return await PublishDueAsync(announcements, unitOfWork, tenantId, cancellationToken);
    }

    /// <summary>
    /// Zamanı gelmiş (ScheduledFor &lt;= constructor'da enjekte edilen TimeProvider'ın "now"u) duyuruları yayınlar.
    /// DI/tenant-scope kurulumundan ayrı tutuldu ki birim testlerde repository/unit-of-work NSubstitute ile,
    /// saat de FixedTimeProvider ile sabitlenip gerçek bir veritabanı veya Thread.Sleep olmadan doğrulanabilsin
    /// (TK-016 kabul kriteri #4).
    /// </summary>
    public async Task<int> PublishDueAsync(
        IAnnouncementRepository announcements, IAnnouncementsUnitOfWork unitOfWork, Guid tenantId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await announcements.GetDueScheduledAsync(tenantId, now, cancellationToken);
        var count = 0;
        foreach (var announcement in due)
        {
            var published = announcement.PublishScheduled(now);
            if (published.IsSuccess)
            {
                count++;
            }
        }

        if (count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return count;
    }

    [LoggerMessage(EventId = 7401, Level = LogLevel.Error, Message = "Scheduled announcement publish failed for tenant {TenantId}")]
    private static partial void LogTenantFailed(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(EventId = 7402, Level = LogLevel.Information, Message = "{Published} scheduled announcement(s) published across {Tenants} tenant(s)")]
    private static partial void LogCompleted(ILogger logger, int published, int tenants);
}

/// <summary>Runs <see cref="PublishScheduledAnnouncementsJob"/> shortly after start and then every minute (Sense.Worker) -
/// frequent enough that a ScheduledFor of "14:32" actually publishes close to that minute, not just that day.</summary>
public sealed partial class PublishScheduledAnnouncementsHostedService(
    PublishScheduledAnnouncementsJob job, TimeProvider clock, ILogger<PublishScheduledAnnouncementsHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(StartupDelay, clock, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await job.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRunFailed(logger, ex);
            }

            await Task.Delay(PollInterval, clock, stoppingToken);
        }
    }

    [LoggerMessage(EventId = 7403, Level = LogLevel.Error, Message = "Scheduled announcement publish run failed")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
