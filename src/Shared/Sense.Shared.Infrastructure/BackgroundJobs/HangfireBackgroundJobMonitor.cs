using Hangfire;
using Hangfire.Storage;
using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>Exposes only recurring jobs created from the application-owned seed catalog.</summary>
public sealed class HangfireBackgroundJobMonitor(JobStorage storage) : IBackgroundJobMonitor
{
    private const int MaximumRecurringJobsRead = 20_000;

    public BackgroundJobRuntimeStatus GetRuntimeStatus()
    {
        var monitoring = storage.GetMonitoringApi();
        var stats = monitoring.GetStatistics();
        return new BackgroundJobRuntimeStatus(
            monitoring.Servers().Count,
            stats.Enqueued,
            stats.Scheduled,
            stats.Processing,
            stats.Failed);
    }

    public IReadOnlyList<RecurringJobStatus> GetRecurringJobs(int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, 100);

        var monitoring = storage.GetMonitoringApi();
        var knownJobs = BackgroundJobSeeding.GetDefaultSeeds().ToDictionary(seed => seed.JobName, StringComparer.Ordinal);
        using var connection = storage.GetConnection();
        var all = connection.GetRecurringJobs().Take(MaximumRecurringJobsRead);
        var catalog = new List<RecurringJobStatus>();

        foreach (var recurring in all)
        {
            if (!TryGetKnownJob(recurring.Id, knownJobs, out var jobName, out var tenantId)) continue;

            catalog.Add(new RecurringJobStatus(
                jobName,
                tenantId,
                recurring.Cron,
                recurring.LastJobState,
                AsUtc(recurring.LastExecution),
                AsUtc(recurring.NextExecution)));
        }

        return catalog
            .OrderBy(job => job.JobName, StringComparer.Ordinal)
            .ThenBy(job => job.TenantId)
            .Skip(skip)
            .Take(take)
            .ToArray();
    }

    private static DateTime? AsUtc(DateTime? value) => value is null
        ? null
        : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static bool TryGetKnownJob(
        string recurringId,
        IReadOnlyDictionary<string, BackgroundJobSeed> knownJobs,
        out string jobName,
        out Guid tenantId)
    {
        foreach (var candidate in knownJobs.Keys)
        {
            var prefix = candidate + "_";
            if (!recurringId.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!Guid.TryParseExact(recurringId[prefix.Length..], "N", out tenantId)) break;
            jobName = candidate;
            return true;
        }

        jobName = string.Empty;
        tenantId = Guid.Empty;
        return false;
    }
}
