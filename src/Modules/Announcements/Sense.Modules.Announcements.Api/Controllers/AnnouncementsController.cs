using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain.Announcements;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

/// <summary>Announcement create/update body. Event fields are required/allowed only for category "event".</summary>
public sealed record AnnouncementRequest(
    string Title,
    string Body,
    AnnouncementCategory Category,
    DateTimeOffset? EventStartsAt,
    DateTimeOffset? EventEndsAt,
    string? Location,
    string? MeetingUrl)
{
    public AnnouncementContent ToContent() =>
        new(Title, Body, Category, EventStartsAt?.UtcDateTime, EventEndsAt?.UtcDateTime, Location, MeetingUrl);
}

/// <summary>Company announcements ("Duyurular") and the upcoming-events feed for the home widget.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/announcements")]
[Authorize]
public sealed class AnnouncementsController : ApiControllerBase
{
    /// <summary>Published announcements: pinned first, newest first.</summary>
    [HttpGet("feed")]
    [ProducesResponseType<PagedResult<AnnouncementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Feed([FromQuery] AnnouncementCategory? category, [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAnnouncementFeedQuery(category, paging), ct));

    /// <summary>HR management list (all statuses).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AnnouncementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] AnnouncementStatus? status, [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAnnouncementsQuery(status, paging), ct));

    /// <summary>Home "Etkinlik takvimi": published events starting in the next <paramref name="days"/> days (default 30).</summary>
    [HttpGet("events/upcoming")]
    [ProducesResponseType<IReadOnlyList<UpcomingEventDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpcomingEvents([FromQuery] int? days, [FromQuery] int? limit, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetUpcomingEventsQuery(days, limit), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<AnnouncementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAnnouncementQuery(id), ct));

    /// <summary>Creates a Draft announcement.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] AnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateAnnouncementCommand(request.ToContent()), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] AnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateAnnouncementCommand(id, request.ToContent()), ct));

    [HttpPost(ApiRoutes.IdParam + "/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new PublishAnnouncementCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ArchiveAnnouncementCommand(id), ct));

    [HttpPost(ApiRoutes.IdParam + "/pin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Pin(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetAnnouncementPinnedCommand(id, true), ct));

    [HttpPost(ApiRoutes.IdParam + "/unpin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unpin(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetAnnouncementPinnedCommand(id, false), ct));
}
