using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Seed data for background jobs.
/// Provides default recurring jobs to be created per-tenant during initialization.
/// </summary>
public static class BackgroundJobSeeding
{
    /// <summary>
    /// Gets the default seed data for background jobs.
    /// These should be created for each tenant on initialization or via admin UI.
    /// </summary>
    /// <returns>List of default background job seeds.</returns>
    public static IEnumerable<BackgroundJobSeed> GetDefaultSeeds()
    {
        yield return new BackgroundJobSeed
        {
            JobName = "DailyAuditSummary",
            JobType = JobType.DailyAuditSummary,
            CronExpression = "0 1 * * *", // 1:00 AM every day
            Description = "Generate daily audit summary for all users",
            Payload = "{\"includeFailures\": true, \"includeSuccesses\": true}"
        };

        yield return new BackgroundJobSeed
        {
            JobName = "WeeklyReportGeneration",
            JobType = JobType.ExportReport,
            CronExpression = "0 9 * * 1", // 9:00 AM every Monday
            Description = "Generate weekly report for management",
            Payload = "{\"reportType\": \"weekly\", \"include\": [\"attendance\", \"payroll\"]}"
        };

        // Faz 1 tur 1: per-tenant daily reminder for documents nearing/past their expiry date
        // (Documents.Infrastructure.BackgroundJobs.DocumentExpiryReminderJobHandler). 06:00 Europe/Istanbul is the
        // intended wall-clock time; IBackgroundJobService.RecurringAsync has no TimeZoneInfo parameter yet (no seed
        // consumer wires GetDefaultSeeds() into RecurringAsync today - see this method's doc comment - so there is
        // nothing to point a time zone at in this slice), so the cron string alone assumes the host's default zone.
        yield return new BackgroundJobSeed
        {
            JobName = "DocumentExpiryReminder",
            JobType = JobType.DocumentExpiryReminder,
            CronExpression = "0 6 * * *", // 06:00 daily (Europe/Istanbul intent, see remark above)
            Description = "Remind about documents expiring soon or already expired",
            Payload = "{}"
        };
    }
}

/// <summary>
/// Data class for background job seed.
/// </summary>
public class BackgroundJobSeed
{
    /// <summary>
    /// Unique name for the recurring job.
    /// </summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Type of job to execute.
    /// </summary>
    public JobType JobType { get; set; }

    /// <summary>
    /// Cron expression for scheduling.
    /// </summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// Description of the job.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Job-specific payload (JSON string).
    /// </summary>
    public string Payload { get; set; } = "{}";
}
