using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Notifications.Application;
using Sense.Modules.Notifications.Application.Notifications;
using Sense.Modules.Notifications.Application.Preferences;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Notifications.Api.Controllers;

public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceInput> Preferences);

/// <summary>The caller's own in-app notifications and notification preferences (self-service only).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
public sealed class NotificationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, [FromQuery] bool unreadOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyNotificationsQuery(paging, unreadOnly), ct));

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyUnreadNotificationCountQuery(), ct));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkNotificationReadCommand(id), ct));

    [HttpPost("read-all")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkAllNotificationsReadCommand(), ct));

    [HttpGet("preferences")]
    [ProducesResponseType<IReadOnlyList<NotificationPreferenceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyNotificationPreferencesQuery(), ct));

    [HttpPut("preferences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateMyNotificationPreferencesCommand(request.Preferences), ct));
}
