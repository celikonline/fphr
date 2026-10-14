namespace Sense.Shared.Contracts.BackgroundJobs;

/// <summary>Operational counters that are safe to expose to platform operators.</summary>
public sealed record BackgroundJobRuntimeStatus(
    int ServerCount,
    long EnqueuedCount,
    long ScheduledCount,
    long ProcessingCount,
    long FailedCount);

/// <summary>Safe projection of a recurring job. Payloads and Hangfire exception details are intentionally omitted.</summary>
public sealed record RecurringJobStatus(
    string JobName,
    Guid TenantId,
    string CronExpression,
    string? LastState,
    DateTime? LastExecutionUtc,
    DateTime? NextExecutionUtc);

/// <summary>Read-only monitoring surface for Hangfire-backed recurring jobs.</summary>
public interface IBackgroundJobMonitor
{
    BackgroundJobRuntimeStatus GetRuntimeStatus();

    IReadOnlyList<RecurringJobStatus> GetRecurringJobs(int skip, int take);
}
