using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.OccupationalSafety.Application.HazardClassifications;
using Sense.Modules.OccupationalSafety.Domain.HazardClassifications;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.OccupationalSafety.Api.Controllers;

public sealed record SetHazardClassificationRequest(Guid LocationId, HazardClass HazardClass, DateOnly EffectiveFrom, string? Note);

/// <summary>Workplace hazard classification per location (6331 sayılı Kanun md.9). No admin-only deletion in this pass - a reclassification appends a new history row (MVP, see handoff doc).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/ohs/hazard-classifications")]
[Authorize]
public sealed class HazardClassificationsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HazardClassificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCurrent(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCurrentHazardClassificationsQuery(), ct));

    [HttpGet("locations/{locationId:guid}/history")]
    [ProducesResponseType<IReadOnlyList<HazardClassificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(Guid locationId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListHazardClassificationHistoryQuery(locationId), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Set([FromBody] SetHazardClassificationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new SetHazardClassificationCommand(request.LocationId, request.HazardClass, request.EffectiveFrom, request.Note), ct));
}
