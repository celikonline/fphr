using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Contracts;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Notifications;

namespace Sense.Modules.Notifications.Infrastructure.Messaging;

/// <summary>
/// Pushes to one user on the shared NotificationsHub (Clients.User). Hosts without SignalR (Worker without a backplane)
/// have no IHubContext registered, so the push is skipped and clients pick the row up via the unread-count poll.
/// </summary>
public sealed class SignalRNotificationRealtimePusher(IServiceProvider services) : INotificationRealtimePusher
{
    public Task PushAsync(Guid recipientUserId, NotificationItemDto notification, CancellationToken ct)
    {
        var hub = services.GetService<IHubContext<NotificationsHub>>();
        return hub is null
            ? Task.CompletedTask
            : hub.Clients.User(recipientUserId.ToString()).SendAsync(NotificationHubMethods.NotificationCreated, notification, ct);
    }
}

/// <summary>SignalR user id = JWT "sub" (the API disables inbound claim mapping, so NameIdentifier is absent).</summary>
public sealed class SubjectClaimUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(ClaimNames.Subject)?.Value ?? connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
