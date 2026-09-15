using Sense.Modules.Notifications.Domain.Notifications;
using Sense.Modules.Notifications.Domain.Preferences;

namespace Sense.Modules.Notifications.Domain;

/// <summary>Domain error codes for the Notifications module (localized in SharedResource).</summary>
public static class NotificationsDomainErrors
{
    public const string RecipientRequired = "notifications.recipient_required";
    public const string TemplateKeyInvalid = "notifications.template_key_invalid";
    public const string LinkTooLong = "notifications.link_too_long";
}

public static class NotificationLimits
{
    public const int TemplateKeyMaxLength = 100;
    public const int LinkMaxLength = 500;
    public const string EmptyArgsJson = "{}";
}

/// <summary>In-app notification repository; every read is scoped to one recipient (self-service only).</summary>
public interface INotificationRepository
{
    void Add(Notification notification);

    Task<Notification?> FindForRecipientAsync(Guid id, Guid recipientUserId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Notification> Items, long TotalCount)> ListForRecipientAsync(
        Guid recipientUserId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default);

    Task<int> MarkAllReadAsync(Guid recipientUserId, DateTime readAt, CancellationToken cancellationToken = default);
}

public interface INotificationPreferenceRepository
{
    void Add(NotificationPreference preference);

    Task<IReadOnlyList<NotificationPreference>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreference>> ListForUsersAsync(
        IReadOnlyCollection<Guid> userIds, string templateKey, CancellationToken cancellationToken = default);
}
