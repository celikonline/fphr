using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

public sealed record CreateSupportTicketRequest(string Subject, string Body, SupportTicketCategory Category, string? SourceRoute);

public sealed record RespondToSupportTicketRequest(string Resolution);

/// <summary>
/// "Yardım Merkezi + Desteğe bağlan" (11.3.13, roadmap #69). Makale arama/okuma izin gerektirmez (herhangi bir
/// kimliği doğrulanmış kullanıcı); talep açma "announcements.my.support_tickets" ile her role otomatik ulaşır;
/// tüm talepleri görme/yanıtlama "announcements.support_tickets.manage" (İK/Admin) gerektirir.
/// senseik-057: Duyurular modülü kapatılsa bile destek her zaman erişilebilir kalır (ModuleAccessExempt).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/support")]
[Authorize]
[ModuleAccessExempt]
public sealed class SupportController : ApiControllerBase
{
    [HttpGet("articles")]
    [ProducesResponseType<IReadOnlyList<HelpArticleSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchArticles([FromQuery] string? q, [FromQuery] string? route, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new SearchHelpArticlesQuery(q, route), ct));

    [HttpGet("articles/{slug}")]
    [ProducesResponseType<HelpArticleDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetArticle(string slug, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetHelpArticleQuery(slug), ct));

    [HttpPost("tickets")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTicket([FromBody] CreateSupportTicketRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateSupportTicketCommand(request.Subject, request.Body, request.Category, request.SourceRoute), ct));

    [HttpGet("tickets/mine")]
    [ProducesResponseType<IReadOnlyList<SupportTicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMySupportTicketsQuery(), ct));

    [HttpGet("tickets/{id:guid}")]
    [ProducesResponseType<SupportTicketDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetSupportTicketQuery(id), ct));

    /// <summary>Kiracının tüm destek talepleri (İK/Admin — "Destek talepleri listesi (platform tarafı)" [Aday] Faz 8'e kadar burası kiracı yöneticisinin görünümüdür).</summary>
    [HttpGet("tickets")]
    [ProducesResponseType<IReadOnlyList<SupportTicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] SupportTicketStatus? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListSupportTicketsQuery(status), ct));

    [HttpPost("tickets/{id:guid}/respond")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToSupportTicketRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RespondToSupportTicketCommand(id, request.Resolution), ct));

    [HttpPost("tickets/{id:guid}/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloseSupportTicketCommand(id), ct));
}
