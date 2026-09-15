using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Modules.Notifications.Domain.UserNotifications;

namespace Sense.Modules.Notifications.Domain;

/// <summary>UserNotification repository (tenant-filtered).</summary>
public interface IUserNotificationRepository
{
    void Add(UserNotification notification);

    Task<bool> ExistsAsync(Guid sourceEventId, string templateKey, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Tracked; null when the notification does not exist or belongs to another user.</summary>
    Task<UserNotification?> FindForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<(IReadOnlyList<UserNotification> Items, long TotalCount)> ListForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default);
}

/// <summary>NotificationDelivery repository (tenant-filtered; the Worker's cross-tenant polling lives in Infrastructure).</summary>
public interface INotificationDeliveryRepository
{
    void Add(NotificationDelivery delivery);

    Task<bool> ExistsAsync(Guid sourceEventId, string templateKey, string channel, string recipientAddress, CancellationToken cancellationToken = default);
}

/// <summary>NotificationPreference repository (tenant-filtered).</summary>
public interface INotificationPreferenceRepository
{
    void Add(NotificationPreference preference);

    /// <summary>Tracked.</summary>
    Task<IReadOnlyList<NotificationPreference>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreference>> ListForUsersAsync(IReadOnlyCollection<Guid> userIds, string templateKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit save for code running outside the command pipeline (integration event handlers, delivery processor) -
/// commands/queries get UnitOfWorkBehaviour instead.
/// </summary>
public interface INotificationsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
