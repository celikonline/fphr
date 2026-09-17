using Microsoft.AspNetCore.Mvc;
using Sense.Shared.Contracts.Commands;
using Sense.Shared.Contracts.Queries;
using Sense.Shared.Web.Authorization;
using Sense.Modules.Announcements.Application.Announcements;

namespace Sense.Modules.Announcements.Api;

[ApiController]
[Route("api/v1/announcements")]
[Authorize(Policy = "Bearer")]
public class AnnouncementsController : ControllerBase
{
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IQueryDispatcher _queryDispatcher;
    private readonly ITenantContext _tenantContext;

    public AnnouncementsController(
        ICommandDispatcher commandDispatcher,
        IQueryDispatcher queryDispatcher,
        ITenantContext tenantContext)
    {
        _commandDispatcher = commandDispatcher;
        _queryDispatcher = queryDispatcher;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    [Authorize("announcements.manage")]
    public async Task<ActionResult<Guid>> CreateAnnouncement(
        [FromBody] CreateAnnouncementCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _commandDispatcher.DispatchAsync(
            command with { TenantId = _tenantContext.TenantId },
            cancellationToken);

        return result.IsSuccess ? Created(string.Empty, result.Value) : BadRequest(result.Error);
    }

    [HttpGet]
    [Authorize("announcements.view")]
    public async Task<ActionResult<List<AnnouncementDto>>> ListAnnouncements(
        [FromQuery] bool? publishedOnly = null,
        CancellationToken cancellationToken = default)
    {
        var query = new ListAnnouncementsQuery(_tenantContext.TenantId, publishedOnly);
        var result = await _queryDispatcher.DispatchAsync(query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
