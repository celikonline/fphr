using Microsoft.EntityFrameworkCore;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Notifications;
using Sense.Modules.Notifications.Domain.Preferences;

namespace Sense.Modules.Notifications.Infrastructure.Persistence;

/// <summary>EF Core notification repository; tenant isolation via ModuleDbContext's global query filter.</summary>
public sealed class NotificationRepository(NotificationsDbContext context) : INotificationRepository
{
    public void Add(Notification notification) => context.Notifications.Add(notification);

    public Task<Notification?> FindForRecipientAsync(Guid id, Guid recipientUserId, CancellationToken cancellationToken = default) =>
        context.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == recipientUserId, cancellationToken);

    public async Task<(IReadOnlyList<Notification> Items, long TotalCount)> ListForRecipientAsync(
        Guid recipientUserId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Notifications.AsNoTracking().Where(n => n.RecipientUserId == recipientUserId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(n => n.OccurredAt).ThenByDescending(n => n.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(Guid recipientUserId, CancellationToken cancellationToken = default) =>
        context.Notifications.CountAsync(n => n.RecipientUserId == recipientUserId && n.ReadAt == null, cancellationToken);

    public async Task<int> MarkAllReadAsync(Guid recipientUserId, DateTime readAt, CancellationToken cancellationToken = default)
    {
        // Tracked update (not ExecuteUpdate) so the audit interceptor stamps ModifiedDate/ModifiedUserId; the unit of work saves.
        var unread = await context.Notifications
            .Where(n => n.RecipientUserId == recipientUserId && n.ReadAt == null)
            .ToListAsync(cancellationToken);
        foreach (var notification in unread)
        {
            notification.MarkRead(readAt);
        }

        return unread.Count;
    }
}

public sealed class NotificationPreferenceRepository(NotificationsDbContext context) : INotificationPreferenceRepository
{
    public void Add(NotificationPreference preference) => context.Preferences.Add(preference);

    public async Task<IReadOnlyList<NotificationPreference>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await context.Preferences.Where(p => p.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationPreference>> ListForUsersAsync(
        IReadOnlyCollection<Guid> userIds, string templateKey, CancellationToken cancellationToken = default)
    {
        var ids = userIds.ToList();
        return await context.Preferences.AsNoTracking()
            .Where(p => p.TemplateKey == templateKey && ids.Contains(p.UserId))
            .ToListAsync(cancellationToken);
    }
}
