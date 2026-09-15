using Hangfire;
using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-based implementation of background job service.
/// </summary>
public class BackgroundJobService : IBackgroundJobService
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<BackgroundJobService> _logger;

    public BackgroundJobService(
        IBackgroundJobClient jobClient,
        IRecurringJobManager recurringJobManager,
        ILogger<BackgroundJobService> logger)
    {
        _jobClient = jobClient;
        _recurringJobManager = recurringJobManager;
        _logger = logger;
    }

    public Task<string> EnqueueAsync(JobType jobType, Guid tenantId, string payload)
    {
        var jobName = $"{jobType}_{tenantId:N}_{Guid.NewGuid():N}";

        try
        {
            var jobId = _jobClient.Enqueue<IBackgroundJobExecutor>(
                executor => executor.ExecuteAsync(jobType, tenantId, payload));

            _logger.LogInformation(
                "Job enqueued. JobId: {JobId}, JobType: {JobType}, TenantId: {TenantId}",
                jobId,
                jobType,
                tenantId);

            return Task.FromResult(jobId ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue job. JobType: {JobType}, TenantId: {TenantId}", jobType, tenantId);
            throw;
        }
    }

    public Task<string> ScheduleAsync(JobType jobType, Guid tenantId, string payload, TimeSpan delay)
    {
        try
        {
            var jobId = _jobClient.Schedule<IBackgroundJobExecutor>(
                executor => executor.ExecuteAsync(jobType, tenantId, payload),
                delay);

            _logger.LogInformation(
                "Job scheduled. JobId: {JobId}, JobType: {JobType}, TenantId: {TenantId}, Delay: {Delay}",
                jobId,
                jobType,
                tenantId,
                delay);

            return Task.FromResult(jobId ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to schedule job. JobType: {JobType}, TenantId: {TenantId}, Delay: {Delay}",
                jobType,
                tenantId,
                delay);
            throw;
        }
    }

    public Task RecurringAsync(string jobName, JobType jobType, Guid tenantId, string cronExpression, string payload)
    {
        try
        {
            var recurringJobId = $"{jobName}_{tenantId:N}";

            _recurringJobManager.AddOrUpdate<IBackgroundJobExecutor>(
                recurringJobId,
                executor => executor.ExecuteAsync(jobType, tenantId, payload),
                cronExpression);

            _logger.LogInformation(
                "Recurring job added/updated. JobName: {JobName}, JobType: {JobType}, TenantId: {TenantId}, Cron: {CronExpression}",
                jobName,
                jobType,
                tenantId,
                cronExpression);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to add/update recurring job. JobName: {JobName}, TenantId: {TenantId}",
                jobName,
                tenantId);
            throw;
        }
    }

    public Task RemoveRecurringAsync(string jobName)
    {
        try
        {
            _recurringJobManager.RemoveIfExists(jobName);

            _logger.LogInformation("Recurring job removed. JobName: {JobName}", jobName);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove recurring job. JobName: {JobName}", jobName);
            throw;
        }
    }
}
