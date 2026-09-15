using System.Globalization;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.Notifications;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.Notifications;

/// <summary>The caller's own notifications, newest first (grid paging; <paramref name="UnreadOnly"/> filter).</summary>
[RequiresPermission(NotificationsPermissions.SelfViewKey)]
public sealed record ListMyNotificationsQuery(PagedQuery Paging, bool UnreadOnly) : IQuery<PagedResult<NotificationItemDto>>;

public sealed class ListMyNotificationsHandler(ICurrentUser currentUser, INotificationRepository notifications, INotificationTemplateRenderer renderer)
    : IQueryHandler<ListMyNotificationsQuery, PagedResult<NotificationItemDto>>
{
    public async Task<Result<PagedResult<NotificationItemDto>>> Handle(ListMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return PagedResult<NotificationItemDto>.Empty(query.Paging);
        }

        var (items, total) = await notifications.ListForRecipientAsync(userId, query.UnreadOnly, query.Paging.Skip, query.Paging.PageSize, cancellationToken);
        var culture = CultureInfo.CurrentUICulture;
        var dtos = items.Select(n => NotificationMapper.ToDto(n, renderer, culture)).ToList();
        return new PagedResult<NotificationItemDto>(dtos, query.Paging.Page, query.Paging.PageSize, total);
    }
}

[RequiresPermission(NotificationsPermissions.SelfViewKey)]
public sealed record GetMyUnreadNotificationCountQuery : IQuery<UnreadCountDto>;

public sealed class GetMyUnreadNotificationCountHandler(ICurrentUser currentUser, INotificationRepository notifications)
    : IQueryHandler<GetMyUnreadNotificationCountQuery, UnreadCountDto>
{
    public async Task<Result<UnreadCountDto>> Handle(GetMyUnreadNotificationCountQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? new UnreadCountDto(await notifications.CountUnreadAsync(userId, cancellationToken))
            : new UnreadCountDto(0);
}

/// <summary>Marks one of the caller's notifications read; someone else's id is NotFound.</summary>
[RequiresPermission(NotificationsPermissions.SelfViewKey)]
public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand;

public sealed class MarkNotificationReadHandler(ICurrentUser currentUser, INotificationRepository notifications, TimeProvider clock)
    : ICommandHandler<MarkNotificationReadCommand>
{
    public async Task<Result> Handle(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        var notification = currentUser.UserId is { } userId
            ? await notifications.FindForRecipientAsync(command.NotificationId, userId, cancellationToken)
            : null;

        if (notification is null)
        {
            return Error.NotFound(NotificationsErrors.NotificationNotFound);
        }

        notification.MarkRead(clock.GetUtcNow().UtcDateTime);
        return Result.Success();
    }
}

[RequiresPermission(NotificationsPermissions.SelfViewKey)]
public sealed record MarkAllNotificationsReadCommand : ICommand<int>;

public sealed class MarkAllNotificationsReadHandler(ICurrentUser currentUser, INotificationRepository notifications, TimeProvider clock)
    : ICommandHandler<MarkAllNotificationsReadCommand, int>
{
    public async Task<Result<int>> Handle(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? await notifications.MarkAllReadAsync(userId, clock.GetUtcNow().UtcDateTime, cancellationToken)
            : 0;
}

public static class NotificationMapper
{
    public static NotificationItemDto ToDto(Notification n, INotificationTemplateRenderer renderer, CultureInfo culture)
    {
        var args = NotificationArgs.Deserialize(n.ArgsJson);
        var rendered = renderer.Render(n.TemplateKey, args, culture);
        return new NotificationItemDto(
            n.Id, n.TemplateKey, JsonNamingPolicyCamel(n.Category.ToString()), rendered.Title, rendered.Body, args, n.Link, n.OccurredAt, n.ReadAt);
    }

    private static string JsonNamingPolicyCamel(string value) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value);
}
