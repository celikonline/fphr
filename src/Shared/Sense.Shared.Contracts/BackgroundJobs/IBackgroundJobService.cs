namespace Sense.Shared.Contracts.BackgroundJobs;

/// <summary>
/// Service for managing background jobs through Hangfire.
/// All operations are tenant-scoped.
/// </summary>
public interface IBackgroundJobService
{
    /// <summary>
    /// Enqueues a job for immediate processing.
    /// </summary>
    /// <param name="jobType">The type of job to enqueue.</param>
    /// <param name="tenantId">The tenant identifier for job isolation.</param>
    /// <param name="payload">Job-specific payload (serialized as JSON).</param>
    /// <returns>The job ID assigned by Hangfire.</returns>
    Task<string> EnqueueAsync(JobType jobType, Guid tenantId, string payload);

    /// <summary>
    /// Schedules a job for delayed execution.
    /// </summary>
    /// <param name="jobType">The type of job to schedule.</param>
    /// <param name="tenantId">The tenant identifier for job isolation.</param>
    /// <param name="payload">Job-specific payload (serialized as JSON).</param>
    /// <param name="delay">The delay before job execution.</param>
    /// <returns>The job ID assigned by Hangfire.</returns>
    Task<string> ScheduleAsync(JobType jobType, Guid tenantId, string payload, TimeSpan delay);

    /// <summary>
    /// Creates or updates a recurring job with a cron expression.
    /// </summary>
    /// <param name="jobName">A unique name for the recurring job (e.g., "DailyAuditSummary").</param>
    /// <param name="jobType">The type of job to execute.</param>
    /// <param name="tenantId">The tenant identifier for job isolation.</param>
    /// <param name="cronExpression">Cron expression (e.g., "0 1 * * *" for 1:00 AM daily).</param>
    /// <param name="payload">Job-specific payload (serialized as JSON).</param>
    /// <remarks>
    /// If a job with the same name already exists, it will be updated.
    /// </remarks>
    Task RecurringAsync(string jobName, JobType jobType, Guid tenantId, string cronExpression, string payload);

    /// <summary>
    /// Removes a recurring job.
    /// </summary>
    /// <param name="jobName">The unique name of the recurring job.</param>
    Task RemoveRecurringAsync(string jobName);
}
