using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

/// <summary>Request to create a review cycle.</summary>
public sealed record CreateReviewCycleRequest(string Name, DateOnly StartDate, DateOnly EndDate);

/// <summary>Performance review cycle (değerlendirme dönemi) management.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/review-cycles")]
[Authorize]
public sealed class ReviewCyclesController : ApiControllerBase
{
    /// <summary>Lists review cycles, optionally filtered to one status.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReviewCycleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListReviewCyclesQuery(status), ct));

    /// <summary>Self-service: the cycles the caller can see are running/finished ("Değerlendirmelerim").</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<ReviewCycleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyReviewCyclesQuery(), ct));

    /// <summary>A single review cycle by id.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<ReviewCycleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetReviewCycleQuery(id), ct));

    /// <summary>Creates a review cycle in Draft status.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateReviewCycleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateReviewCycleCommand(request.Name, request.StartDate, request.EndDate), ct));

    /// <summary>Activates a review cycle (Draft -> Active).</summary>
    [HttpPost(ApiRoutes.IdParam + "/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ActivateReviewCycleCommand(id), ct));

    /// <summary>Closes a review cycle (Active -> Closed, terminal).</summary>
    [HttpPost(ApiRoutes.IdParam + "/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloseReviewCycleCommand(id), ct));
}
