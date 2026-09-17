using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// G-1-13: <see cref="BackgroundJobSeeding.GetDefaultSeeds"/> önceden hiçbir üretim kodundan çağrılmıyordu -
/// KVKK md.7 imha/hatırlatma işleri (<see cref="JobType.DocumentExpiryReminder"/>,
/// <see cref="JobType.CandidateRetentionSweep"/> dahil) fiilen hiç koşmuyordu. Bu sınıf API başlangıcında her
/// yazılabilir kiracı için tüm seed'leri <see cref="IBackgroundJobService.RecurringAsync"/> ile kaydeder/günceller.
/// İdempotent: <see cref="BackgroundJobService.RecurringAsync"/> Hangfire'ın <c>AddOrUpdate</c> semantiğine göre
/// deterministik <c>{jobName}_{tenantId:N}</c> kimliği kullanır - aynı çift için tekrar çağrı kopya üretmez,
/// yalnız var olan yinelenen işi günceller.
/// </summary>
public sealed partial class BackgroundJobSeedSynchronizer(
    IActiveTenantDirectory tenants,
    IBackgroundJobService jobs,
    ILogger<BackgroundJobSeedSynchronizer> logger)
{
    /// <returns>Kaydedilen/güncellenen toplam yinelenen iş sayısı (kiracı sayısı × seed sayısı).</returns>
    public async Task<int> SyncAllTenantsAsync(CancellationToken ct)
    {
        var tenantIds = await tenants.GetActiveTenantIdsAsync(ct).ConfigureAwait(false);
        var seeds = BackgroundJobSeeding.GetDefaultSeeds().ToList();
        var registered = 0;

        foreach (var tenantId in tenantIds)
        {
            foreach (var seed in seeds)
            {
                await jobs.RecurringAsync(seed.JobName, seed.JobType, tenantId, seed.CronExpression, seed.Payload).ConfigureAwait(false);
                registered++;
            }
        }

        LogSynced(logger, tenantIds.Count, seeds.Count, registered);
        return registered;
    }

    [LoggerMessage(EventId = 2110, Level = LogLevel.Information,
        Message = "Background job seeds synchronized: {TenantCount} tenant(s) x {SeedCount} seed(s) = {TotalRegistered} recurring job(s) registered/updated")]
    private static partial void LogSynced(ILogger logger, int tenantCount, int seedCount, int totalRegistered);
}

/// <summary>API başlangıcında (seed'den sonra) arka plan işi tohumlarını bir kez senkronize eder; hata başlatmayı durdurmaz.</summary>
public sealed partial class BackgroundJobSeedSyncHostedService(IServiceScopeFactory scopes, ILogger<BackgroundJobSeedSyncHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var synchronizer = scope.ServiceProvider.GetRequiredService<BackgroundJobSeedSynchronizer>();
            await synchronizer.SyncAllTenantsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Veritabanı henüz migrate edilmemiş olabilir (Migrator ayrı çalışır); bir sonraki başlatmada tekrar denenir.
            LogSyncFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 2111, Level = LogLevel.Warning, Message = "Background job seed synchronization failed; it will be retried on next start")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);
}
