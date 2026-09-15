using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Notifications.Application.Preferences;
using Sense.Modules.Notifications.Application.UserNotifications;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Notifications.Api.Controllers;

public sealed record UpdateNotificationPreferencesBody(IReadOnlyList<NotificationPreferenceInput> Items);

/// <summary>The caller's own in-app notifications (bell / home page) and notification settings.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/notifications/my")]
[Authorize]
public sealed class MyNotificationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<MyNotificationsPageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = PagingDefaults.FirstPage,
        [FromQuery] int pageSize = ListMyNotificationsQuery.DefaultPageSize,
        [FromQuery] bool unreadOnly = false,
        CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListMyNotificationsQuery(page, pageSize, unreadOnly), ct));

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyUnreadCountQuery(), ct));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkNotificationReadCommand(id), ct));

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkAllNotificationsReadCommand(), ct));

    [HttpGet("preferences")]
    [ProducesResponseType<IReadOnlyList<NotificationPreferenceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preferences(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyNotificationPreferencesQuery(), ct));

    [HttpPut("preferences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesBody body, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateMyNotificationPreferencesCommand(body.Items), ct));
}
