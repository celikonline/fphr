using Microsoft.EntityFrameworkCore;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.Preferences;
using Sense.Modules.Notifications.Domain.UserNotifications;

namespace Sense.Modules.Notifications.Infrastructure.Persistence;

/// <summary>EF Core-backed UserNotification repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class UserNotificationRepository(NotificationsDbContext context) : IUserNotificationRepository
{
    public void Add(UserNotification notification) => context.UserNotifications.Add(notification);

    public Task<bool> ExistsAsync(Guid sourceEventId, string templateKey, Guid userId, CancellationToken cancellationToken = default) =>
        context.UserNotifications.AnyAsync(
            n => n.SourceEventId == sourceEventId && n.TemplateKey == templateKey && n.UserId == userId, cancellationToken);

    public Task<UserNotification?> FindForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
        context.UserNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<(IReadOnlyList<UserNotification> Items, long TotalCount)> ListForUserAsync(
        Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = context.UserNotifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.UserNotifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default) =>
        context.UserNotifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now).SetProperty(n => n.ModifiedDate, now), cancellationToken);
}

/// <summary>EF Core-backed NotificationDelivery repository with tenant isolation.</summary>
public sealed class NotificationDeliveryRepository(NotificationsDbContext context) : INotificationDeliveryRepository
{
    public void Add(NotificationDelivery delivery) => context.Deliveries.Add(delivery);

    public Task<bool> ExistsAsync(Guid sourceEventId, string templateKey, string channel, string recipientAddress, CancellationToken cancellationToken = default) =>
        context.Deliveries.AnyAsync(
            d => d.SourceEventId == sourceEventId && d.TemplateKey == templateKey && d.Channel == channel && d.RecipientAddress == recipientAddress,
            cancellationToken);
}

/// <summary>EF Core-backed NotificationPreference repository with tenant isolation.</summary>
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
            .Where(p => ids.Contains(p.UserId) && p.TemplateKey == templateKey)
            .ToListAsync(cancellationToken);
    }
}
