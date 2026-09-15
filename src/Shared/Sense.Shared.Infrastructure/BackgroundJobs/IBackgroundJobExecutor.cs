using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Executor interface for Hangfire jobs.
/// Must be public and have public methods for Hangfire to invoke.
/// </summary>
public interface IBackgroundJobExecutor
{
    /// <summary>
    /// Executes a background job.
    /// </summary>
    /// <param name="jobType">The type of job to execute.</param>
    /// <param name="tenantId">The tenant identifier (for isolation).</param>
    /// <param name="payload">Job-specific payload (JSON string).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteAsync(JobType jobType, Guid tenantId, string payload);
}
