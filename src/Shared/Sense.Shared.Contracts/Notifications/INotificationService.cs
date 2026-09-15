namespace Sense.Shared.Contracts.Notifications;

/// <summary>
/// Service for broadcasting real-time notifications to clients via SignalR.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a notification to all connected clients in a tenant group.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="notification">The notification to send.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendToTenantAsync(Guid tenantId, NotificationDto notification);

    /// <summary>
    /// Broadcasts job progress to all connected clients in a tenant group.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="progress">The job progress information.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BroadcastJobProgressAsync(Guid tenantId, JobProgressDto progress);

    /// <summary>
    /// Broadcasts audit alert to all connected clients in a tenant group.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="alert">The audit alert information.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task BroadcastAuditAlertAsync(Guid tenantId, AuditAlertDto alert);
}
