using Microsoft.EntityFrameworkCore;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Deliveries;
using Sense.Modules.Notifications.Domain.HrMessages;
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

/// <summary>EF Core-backed HrMessage repository with tenant isolation.</summary>
public sealed class HrMessageRepository(NotificationsDbContext context) : IHrMessageRepository
{
    public void Add(HrMessage message) => context.HrMessages.Add(message);

    public async Task<(IReadOnlyList<HrMessage> Items, long TotalCount)> ListInboxAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = context.HrMessages.AsNoTracking()
            .Where(m => context.HrMessageRecipients.Any(r => r.MessageId == m.Id && r.UserId == userId));
        return await PageAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<(IReadOnlyList<HrMessage> Items, long TotalCount)> ListSentAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        await PageAsync(context.HrMessages.AsNoTracking().Where(m => m.SenderUserId == userId), page, pageSize, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountRecipientsAsync(IReadOnlyCollection<Guid> messageIds, CancellationToken cancellationToken = default)
    {
        var ids = messageIds.ToList();
        return await context.HrMessageRecipients.AsNoTracking()
            .Where(r => ids.Contains(r.MessageId))
            .GroupBy(r => r.MessageId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    private static async Task<(IReadOnlyList<HrMessage> Items, long TotalCount)> PageAsync(
        IQueryable<HrMessage> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}

/// <summary>EF Core-backed MessageGroup repository with tenant isolation.</summary>
public sealed class MessageGroupRepository(NotificationsDbContext context) : IMessageGroupRepository
{
    public void Add(MessageGroup group) => context.MessageGroups.Add(group);

    public void Remove(MessageGroup group) => context.MessageGroups.Remove(group);

    public Task<MessageGroup?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.MessageGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MessageGroup>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.ToList();
        return await context.MessageGroups.AsNoTracking().Where(g => list.Contains(g.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MessageGroup>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.MessageGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(cancellationToken);

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken cancellationToken = default) =>
        context.MessageGroups.AnyAsync(g => g.Name == name && (exceptId == null || g.Id != exceptId), cancellationToken);
}
