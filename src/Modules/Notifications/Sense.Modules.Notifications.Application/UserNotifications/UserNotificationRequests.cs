using FluentValidation;
using Sense.Modules.Notifications.Domain;
using Sense.Modules.Notifications.Domain.UserNotifications;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Application.UserNotifications;

public sealed record UserNotificationDto(
    Guid Id, string TemplateKey, string Title, string Body, string? ActionUrl, DateTime CreatedAt, DateTime? ReadAt)
{
    public static UserNotificationDto From(UserNotification n) =>
        new(n.Id, n.TemplateKey, n.Title, n.Body, n.ActionUrl, n.CreatedAt, n.ReadAt);
}

public sealed record MyNotificationsPageDto(IReadOnlyList<UserNotificationDto> Items, int Page, int PageSize, long TotalCount, int UnreadCount);

public sealed record UnreadCountDto(int Count);

/// <summary>The caller's in-app notifications, newest first.</summary>
[RequiresPermission("notifications.my.view")]
public sealed record ListMyNotificationsQuery(int Page = PagingDefaults.FirstPage, int PageSize = ListMyNotificationsQuery.DefaultPageSize, bool UnreadOnly = false)
    : IQuery<MyNotificationsPageDto>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed class ListMyNotificationsValidator : AbstractValidator<ListMyNotificationsQuery>
{
    public ListMyNotificationsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(PagingDefaults.FirstPage);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ListMyNotificationsQuery.MaxPageSize);
    }
}

public sealed class ListMyNotificationsHandler(ICurrentUser currentUser, IUserNotificationRepository notifications)
    : IQueryHandler<ListMyNotificationsQuery, MyNotificationsPageDto>
{
    public async Task<Result<MyNotificationsPageDto>> Handle(ListMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new MyNotificationsPageDto([], query.Page, query.PageSize, 0, 0);
        }

        var (items, total) = await notifications.ListForUserAsync(userId, query.UnreadOnly, query.Page, query.PageSize, cancellationToken);
        var unread = await notifications.CountUnreadAsync(userId, cancellationToken);
        return new MyNotificationsPageDto(items.Select(UserNotificationDto.From).ToList(), query.Page, query.PageSize, total, unread);
    }
}

/// <summary>Unread badge count for the caller (polled/refreshed by the bell).</summary>
[RequiresPermission("notifications.my.view")]
public sealed record GetMyUnreadCountQuery : IQuery<UnreadCountDto>;

public sealed class GetMyUnreadCountHandler(ICurrentUser currentUser, IUserNotificationRepository notifications)
    : IQueryHandler<GetMyUnreadCountQuery, UnreadCountDto>
{
    public async Task<Result<UnreadCountDto>> Handle(GetMyUnreadCountQuery query, CancellationToken cancellationToken) =>
        currentUser.UserId is { } userId
            ? new UnreadCountDto(await notifications.CountUnreadAsync(userId, cancellationToken))
            : new UnreadCountDto(0);
}

[RequiresPermission("notifications.my.manage")]
public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand;

public sealed class MarkNotificationReadValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadValidator() => RuleFor(x => x.NotificationId).NotEmpty();
}

public sealed class MarkNotificationReadHandler(ICurrentUser currentUser, IUserNotificationRepository notifications, TimeProvider clock)
    : ICommandHandler<MarkNotificationReadCommand>
{
    public async Task<Result> Handle(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        var notification = currentUser.UserId is { } userId
            ? await notifications.FindForUserAsync(command.NotificationId, userId, cancellationToken)
            : null;
        if (notification is null)
        {
            return Error.NotFound(NotificationsErrors.NotificationNotFound);
        }

        notification.MarkRead(clock.GetUtcNow().UtcDateTime);
        return Result.Success();
    }
}

[RequiresPermission("notifications.my.manage")]
public sealed record MarkAllNotificationsReadCommand : ICommand;

public sealed class MarkAllNotificationsReadHandler(ICurrentUser currentUser, IUserNotificationRepository notifications, TimeProvider clock)
    : ICommandHandler<MarkAllNotificationsReadCommand>
{
    public async Task<Result> Handle(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is { } userId)
        {
            await notifications.MarkAllReadAsync(userId, clock.GetUtcNow().UtcDateTime, cancellationToken);
        }

        return Result.Success();
    }
}
