using System.Diagnostics;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.Observability;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire job executor. Handles job context setup (tenant) and execution.
/// </summary>
public class BackgroundJobExecutor : IBackgroundJobExecutor
{
    private readonly ITenantContextSetter _tenantContextSetter;
    private readonly ICorrelationIdContextSetter _correlationIdContextSetter;
    private readonly INotificationService _notificationService;
    private readonly ILogger<BackgroundJobExecutor> _logger;
    private readonly IReadOnlyDictionary<JobType, IScheduledJobHandler> _handlers;

    public BackgroundJobExecutor(
        ITenantContextSetter tenantContextSetter,
        ICorrelationIdContextSetter correlationIdContextSetter,
        INotificationService notificationService,
        IEnumerable<IScheduledJobHandler> handlers,
        ILogger<BackgroundJobExecutor> logger)
    {
        _tenantContextSetter = tenantContextSetter;
        _correlationIdContextSetter = correlationIdContextSetter;
        _notificationService = notificationService;
        _logger = logger;
        _handlers = BuildHandlerMap(handlers);
    }

    /// <summary>ADR-0004: aynı <see cref="JobType"/> için iki işleyici kayıtlıysa fail-fast (yanlış modül DI kaydı, sessizce birini yutmak yerine hemen patlar).</summary>
    private static IReadOnlyDictionary<JobType, IScheduledJobHandler> BuildHandlerMap(IEnumerable<IScheduledJobHandler> handlers)
    {
        var map = new Dictionary<JobType, IScheduledJobHandler>();
        foreach (var handler in handlers)
        {
            if (!map.TryAdd(handler.Handles, handler))
            {
                throw new InvalidOperationException(
                    $"Job type '{handler.Handles}' için birden fazla IScheduledJobHandler kayıtlı: '{map[handler.Handles].GetType().FullName}' ve '{handler.GetType().FullName}'.");
            }
        }

        return map;
    }

    public async Task ExecuteAsync(JobType jobType, Guid tenantId, string payload)
    {
        // Job'un kendi correlation id'si: tetikleyen istekten bağımsız, iş için tek başına iz sürülebilir bir kimlik.
        var correlationId = $"job-{Guid.NewGuid():N}";
        using var correlationScope = _correlationIdContextSetter.BeginScope(correlationId);
        using var tenantScope = _tenantContextSetter.BeginScope(tenantId);
        using var activity = SenseActivitySources.BackgroundJobs.StartActivity(
            $"job.{jobType}".ToLowerInvariant(),
            ActivityKind.Internal);
        activity?.SetTag("sense.job.type", jobType.ToString());
        activity?.SetTag("sense.tenant.id", tenantId);
        activity?.SetTag(SenseActivitySources.Tags.CorrelationId, correlationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Starting background job execution. JobType: {JobType}, TenantId: {TenantId}",
                jobType,
                tenantId);

            // Dispatch to job-specific handler
            await ExecuteJobAsync(jobType, tenantId, payload);

            stopwatch.Stop();
            RecordDuration(jobType, stopwatch.Elapsed, SenseMeters.Outcomes.Success);

            _logger.LogInformation(
                "Background job completed successfully. JobType: {JobType}, TenantId: {TenantId}, Duration: {DurationMs}ms",
                jobType,
                tenantId,
                stopwatch.ElapsedMilliseconds);

            // Notify clients of job completion
            var notification = NotificationDto.Create(
                title: $"{jobType} Completed",
                message: $"Job {jobType} completed successfully in {stopwatch.ElapsedMilliseconds}ms",
                type: NotificationType.Success,
                urgency: NotificationUrgency.Low);

            await _notificationService.SendToTenantAsync(tenantId, notification);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordDuration(jobType, stopwatch.Elapsed, SenseMeters.Outcomes.Exception);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(
                ex,
                "Background job failed. JobType: {JobType}, TenantId: {TenantId}, Duration: {DurationMs}ms",
                jobType,
                tenantId,
                stopwatch.ElapsedMilliseconds);

            // Notify clients of job failure
            var notification = NotificationDto.Create(
                title: $"{jobType} Failed",
                message: $"Job {jobType} failed: {ex.Message}",
                type: NotificationType.Error,
                urgency: NotificationUrgency.High);

            await _notificationService.SendToTenantAsync(tenantId, notification);

            throw;
        }
    }

    private static void RecordDuration(JobType jobType, TimeSpan elapsed, string outcome) =>
        SenseMeters.BackgroundJobDuration.Record(
            elapsed.TotalSeconds,
            new KeyValuePair<string, object?>(SenseMeters.Tags.JobType, jobType.ToString()),
            new KeyValuePair<string, object?>(SenseMeters.Tags.Outcome, outcome));

    /// <summary>
    /// ADR-0004: merkezi switch yerine <see cref="JobType"/>'a kayıtlı <see cref="IScheduledJobHandler"/>'a dispatch eder.
    /// Henüz hiçbir modül bir <see cref="JobType"/> için işleyici kaydetmemişse uyarı loglanır ve no-op döner
    /// (Hangfire yeniden denemesin diye exception atılmaz; eksik kayıt bir kod hatasıdır, çalışma zamanı hatası değil).
    /// </summary>
    private async Task ExecuteJobAsync(JobType jobType, Guid tenantId, string payload)
    {
        if (!_handlers.TryGetValue(jobType, out var handler))
        {
            _logger.LogWarning(
                "Job type {JobType} için kayıtlı IScheduledJobHandler yok; job atlanıyor. TenantId: {TenantId}",
                jobType,
                tenantId);
            return;
        }

        var context = new ScheduledJobContext(tenantId, payload, DateTimeOffset.UtcNow);
        await handler.ExecuteAsync(context, CancellationToken.None);
    }
}
