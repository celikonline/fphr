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
        // intended wall-clock time; IBackgroundJobService.RecurringAsync has no TimeZoneInfo parameter (G-1-13:
        // BackgroundJobSeedSynchronizer now wires this into RecurringAsync per active tenant at API startup), so
        // the cron string alone assumes the host's default zone - no zone is passed anywhere in this slice.
        yield return new BackgroundJobSeed
        {
            JobName = "DocumentExpiryReminder",
            JobType = JobType.DocumentExpiryReminder,
            CronExpression = "0 6 * * *", // 06:00 daily (Europe/Istanbul intent, see remark above)
            Description = "Remind about documents expiring soon or already expired",
            Payload = "{}"
        };

        // Faz 1 / Faz 9 tur 1: per-tenant daily sweep that anonymizes/retires candidate data past its retention
        // window (Faz 9's Hired-filtered consumer lands in the same push as this seed - see PANO.md G-9-09).
        // 03:30 Europe/Istanbul is the intended wall-clock time; same caveat as DocumentExpiryReminder above about
        // IBackgroundJobService.RecurringAsync having no TimeZoneInfo parameter.
        yield return new BackgroundJobSeed
        {
            JobName = "CandidateRetentionSweep",
            JobType = JobType.CandidateRetentionSweep,
            CronExpression = "30 3 * * *", // 03:30 daily (Europe/Istanbul intent, see remark above)
            Description = "Sweep candidate records past their retention window for anonymization",
            Payload = "{}"
        };

        // Faz 6 tur 1: per-tenant daily automatic locking of shift periods that reached their due date
        // (Attendance.Application.Shifts.LockDueShiftPeriodsCommand / LockDueShiftPeriodsHandler).
        // 02:00 Europe/Istanbul is the intended wall-clock time; same TimeZoneInfo caveat applies.
        yield return new BackgroundJobSeed
        {
            JobName = "ShiftPeriodAutoLock",
            JobType = JobType.ShiftPeriodAutoLock,
            CronExpression = "0 2 * * *", // 02:00 daily (Europe/Istanbul intent, see remark above)
            Description = "Automatically lock shift periods that reached their due date",
            Payload = "{}"
        };

        // İSG: per-tenant daily reminder for work accidents whose SGK notification deadline (3rd business day,
        // 5510 md.13) is today/tomorrow or already missed (OccupationalSafety SgkNotificationReminderJobHandler).
        // 08:00 Europe/Istanbul intent; same TimeZoneInfo caveat as the seeds above.
        yield return new BackgroundJobSeed
        {
            JobName = "OhsSgkNotificationReminder",
            JobType = JobType.OhsSgkNotificationReminder,
            CronExpression = "0 8 * * *", // 08:00 daily (Europe/Istanbul intent, see remark above)
            Description = "Remind about work accidents approaching or past their SGK notification deadline",
            Payload = "{}"
        };

        // Birthday greetings: per-tenant daily pass that publishes today's birthdays (Employee
        // BirthdayGreetingJobHandler) for Notifications' e-mail + in-app greeting. 09:00 Europe/Istanbul intent;
        // same TimeZoneInfo caveat as the seeds above.
        yield return new BackgroundJobSeed
        {
            JobName = "EmployeeBirthdayGreeting",
            JobType = JobType.EmployeeBirthdayGreeting,
            CronExpression = "0 9 * * *", // 09:00 daily (Europe/Istanbul intent, see remark above)
            Description = "Send birthday greetings to employees whose birthday is today",
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
