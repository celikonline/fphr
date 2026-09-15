using System.Diagnostics;
using System.Text.Json;
using Hangfire;
using Sense.Shared.Contracts.BackgroundJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Shared.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire job executor. Handles job context setup (tenant) and execution.
/// </summary>
public class BackgroundJobExecutor : IBackgroundJobExecutor
{
    private readonly ITenantContextSetter _tenantContextSetter;
    private readonly INotificationService _notificationService;
    private readonly ILogger<BackgroundJobExecutor> _logger;

    public BackgroundJobExecutor(
        ITenantContextSetter tenantContextSetter,
        INotificationService notificationService,
        ILogger<BackgroundJobExecutor> logger)
    {
        _tenantContextSetter = tenantContextSetter;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(JobType jobType, Guid tenantId, string payload)
    {
        using (_tenantContextSetter.BeginScope(tenantId))
        {
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
    }

    /// <summary>
    /// Routes job to appropriate handler based on job type.
    /// </summary>
    private async Task ExecuteJobAsync(JobType jobType, Guid tenantId, string payload)
    {
        switch (jobType)
        {
            case JobType.SendEmail:
                await ExecuteSendEmailAsync(payload);
                break;

            case JobType.ExportReport:
                await ExecuteExportReportAsync(payload);
                break;

            case JobType.SyncAttendance:
                await ExecuteSyncAttendanceAsync(payload);
                break;

            case JobType.ApprovalReminder:
                await ExecuteApprovalReminderAsync(payload);
                break;

            case JobType.PayrollRun:
                await ExecutePayrollRunAsync(payload);
                break;

            case JobType.DailyAuditSummary:
                await ExecuteDailyAuditSummaryAsync(payload);
                break;

            default:
                throw new NotSupportedException($"Job type {jobType} is not supported");
        }
    }

    private Task ExecuteSendEmailAsync(string payload)
    {
        _logger.LogInformation("Executing SendEmail job. Payload: {Payload}", payload);
        // TODO: Implement email sending logic
        return Task.CompletedTask;
    }

    private Task ExecuteExportReportAsync(string payload)
    {
        _logger.LogInformation("Executing ExportReport job. Payload: {Payload}", payload);
        // TODO: Implement report export logic
        return Task.CompletedTask;
    }

    private Task ExecuteSyncAttendanceAsync(string payload)
    {
        _logger.LogInformation("Executing SyncAttendance job. Payload: {Payload}", payload);
        // TODO: Implement attendance sync logic
        return Task.CompletedTask;
    }

    private Task ExecuteApprovalReminderAsync(string payload)
    {
        _logger.LogInformation("Executing ApprovalReminder job. Payload: {Payload}", payload);
        // TODO: Implement approval reminder logic
        return Task.CompletedTask;
    }

    private Task ExecutePayrollRunAsync(string payload)
    {
        _logger.LogInformation("Executing PayrollRun job. Payload: {Payload}", payload);
        // TODO: Implement payroll run logic
        return Task.CompletedTask;
    }

    private Task ExecuteDailyAuditSummaryAsync(string payload)
    {
        _logger.LogInformation("Executing DailyAuditSummary job. Payload: {Payload}", payload);
        // TODO: Implement daily audit summary logic
        return Task.CompletedTask;
    }
}
