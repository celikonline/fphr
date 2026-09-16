using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Recruitment.Application.Tags;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Recruitment.Api.Controllers;

/// <summary>Request to create a tag.</summary>
public sealed record CreateTagRequest(string Name);

/// <summary>Candidate tag catalog (R2, roadmap #89 - manual catalog; auto-tagging from application answers is R3).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/recruitment/tags")]
[Authorize]
public sealed class TagsController : ApiControllerBase
{
    /// <summary>Lists the tenant's candidate tag catalog.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TagDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListTagsQuery(activeOnly), ct));

    /// <summary>Creates a tag.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateTagCommand(request.Name), ct));

    /// <summary>Deactivates a tag.</summary>
    [HttpPost(ApiRoutes.IdParam + "/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateTagCommand(id), ct));

    /// <summary>Reactivates a previously deactivated tag.</summary>
    [HttpPost(ApiRoutes.IdParam + "/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReactivateTagCommand(id), ct));
}
