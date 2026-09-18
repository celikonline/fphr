using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

public sealed record CreateAnnouncementRequest(string Title, string Content, AnnouncementType Type, AnnouncementPriority Priority, string? TargetAudience);

public sealed record PublishAnnouncementRequest(DateTime? ExpiresAt);

/// <summary>Kurumsal duyurular (roadmap: Announcements modülü). Yayınlanmış duyurular Faz 1 ana sayfa akışında listelenir.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/announcements")]
[Authorize]
public sealed class AnnouncementsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AnnouncementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool? publishedOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAnnouncementsQuery(publishedOnly), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<AnnouncementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAnnouncementQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateAnnouncementCommand(request.Title, request.Content, request.Type, request.Priority, request.TargetAudience), ct));

    [HttpPost(ApiRoutes.IdParam + "/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new PublishAnnouncementCommand(id, request.ExpiresAt), ct));

    [HttpPost(ApiRoutes.IdParam + "/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ArchiveAnnouncementCommand(id), ct));
}
