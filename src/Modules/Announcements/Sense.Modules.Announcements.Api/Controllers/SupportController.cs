using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Domain.Support;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

public sealed record CreateSupportTicketRequest(string Subject, string Body, SupportTicketCategory Category, string? PageContext);

/// <summary>
/// Support sub-area of Announcements: read-only platform Help Center (articles are identical for every tenant; no
/// tenant-facing editor - platform console is Faz 8) and "Desteğe bağlan" tickets (own tickets only).
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/support")]
[Authorize]
public sealed class SupportController : ApiControllerBase
{
    /// <summary>Contact e-mail and help video channel for the Destek page.</summary>
    [HttpGet("info")]
    [ProducesResponseType<SupportInfoDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Info(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetSupportInfoQuery(), ct));

    /// <summary>Help categories for <paramref name="locale"/> ("tr"/"en"; defaults to the request culture).</summary>
    [HttpGet("help/categories")]
    [ProducesResponseType<IReadOnlyList<HelpCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories([FromQuery] string? locale, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListHelpCategoriesQuery(locale), ct));

    /// <summary>Keyword search (best match first) or browse (empty q) over published articles.</summary>
    [HttpGet("help/articles")]
    [ProducesResponseType<IReadOnlyList<HelpArticleSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Articles(
        [FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] string? locale, [FromQuery] int? limit, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new SearchHelpArticlesQuery(q, categoryId, locale, limit), ct));

    [HttpGet("help/articles/{slug}")]
    [ProducesResponseType<HelpArticleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Article(string slug, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetHelpArticleQuery(slug), ct));

    /// <summary>Opens a support ticket (e-mailed to the support inbox).</summary>
    [HttpPost("tickets")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTicket([FromBody] CreateSupportTicketRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateSupportTicketCommand(request.Subject, request.Body, request.Category, request.PageContext), ct));

    /// <summary>The caller's own tickets ("Taleplerim"), newest first.</summary>
    [HttpGet("tickets/my")]
    [ProducesResponseType<PagedResult<SupportTicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTickets([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMySupportTicketsQuery(paging), ct));
}
