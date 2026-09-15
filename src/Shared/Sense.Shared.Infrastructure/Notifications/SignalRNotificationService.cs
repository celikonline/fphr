using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sense.Shared.Contracts.Notifications;

namespace Sense.Shared.Infrastructure.Notifications;

/// <summary>
/// SignalR-based implementation of notification service.
/// Broadcasts notifications to tenant groups.
/// </summary>
public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext _hubContext;
    private readonly ILogger<SignalRNotificationService> _logger;

    /// <summary>
    /// Initializes a new instance of the SignalRNotificationService.
    /// The hub context is obtained through DI without explicit hub type reference.
    /// </summary>
    public SignalRNotificationService(
        IHubContext hubContext,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendToTenantAsync(Guid tenantId, NotificationDto notification)
    {
        try
        {
            var groupName = SignalRGroupNames.ForTenant(tenantId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ReceiveNotification", notification.Title, notification.Message, notification.Type, notification.Urgency);

            _logger.LogInformation(
                "Notification sent to tenant group. TenantId: {TenantId}, GroupName: {GroupName}, Title: {Title}",
                tenantId,
                groupName,
                notification.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send notification to tenant. TenantId: {TenantId}, Title: {Title}",
                tenantId,
                notification.Title);
            throw;
        }
    }

    public async Task BroadcastJobProgressAsync(Guid tenantId, JobProgressDto progress)
    {
        try
        {
            var groupName = SignalRGroupNames.ForTenant(tenantId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ReceiveJobProgress", progress.JobName, progress.PercentComplete, progress.Status);

            _logger.LogInformation(
                "Job progress broadcasted to tenant group. TenantId: {TenantId}, JobName: {JobName}, Progress: {Progress}%",
                tenantId,
                progress.JobName,
                progress.PercentComplete);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast job progress to tenant. TenantId: {TenantId}, JobName: {JobName}",
                tenantId,
                progress.JobName);
            throw;
        }
    }

    public async Task BroadcastAuditAlertAsync(Guid tenantId, AuditAlertDto alert)
    {
        try
        {
            var groupName = SignalRGroupNames.ForTenant(tenantId);

            await _hubContext.Clients
                .Group(groupName)
                .SendAsync("ReceiveAuditAlert", alert.Action, alert.Resource, alert.Actor, alert.Timestamp);

            _logger.LogInformation(
                "Audit alert broadcasted to tenant group. TenantId: {TenantId}, Action: {Action}, Resource: {Resource}",
                tenantId,
                alert.Action,
                alert.Resource);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to broadcast audit alert to tenant. TenantId: {TenantId}, Action: {Action}",
                tenantId,
                alert.Action);
            throw;
        }
    }
}
