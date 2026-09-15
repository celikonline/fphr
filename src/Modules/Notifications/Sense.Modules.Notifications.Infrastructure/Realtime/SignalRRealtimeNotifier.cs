using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.UserNotifications;
using Sense.Modules.Notifications.Contracts;
using Sense.Shared.Contracts.Notifications;
using Sense.Shared.Infrastructure.Notifications;

namespace Sense.Modules.Notifications.Infrastructure.Realtime;

/// <summary>
/// Sends to the "user-{id:N}" group NotificationsHub joins every connection to. From Sense.Worker this goes over the
/// Redis backplane to the Api's connections; without Redis nothing reaches clients and the web bell falls back to
/// polling the unread count. Failures are logged, never thrown - the notification row is already saved.
/// </summary>
public sealed partial class SignalRRealtimeNotifier(IHubContext<NotificationsHub> hub, ILogger<SignalRRealtimeNotifier> logger) : IRealtimeNotifier
{
    public async Task NotifyUserAsync(Guid userId, UserNotificationDto notification, CancellationToken ct = default)
    {
        try
        {
            await hub.Clients.Group(SignalRGroupNames.ForUser(userId))
                .SendAsync(NotificationsHubMethods.UserNotificationReceived, notification, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.PushFailed(logger, ex, notification.Id, userId);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 4200, Level = LogLevel.Warning, Message = "Realtime push of notification {NotificationId} to user {UserId} failed")]
        public static partial void PushFailed(ILogger logger, Exception exception, Guid notificationId, Guid userId);
    }
}
