using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewForms;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

/// <summary>Request to create a review form.</summary>
public sealed record CreateReviewFormRequest(Guid ReviewCycleId, string Title, IReadOnlyList<ReviewFormQuestionInput> Questions);

/// <summary>Review form (form + soru/ağırlık) management, scoped to a review cycle.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/review-forms")]
[Authorize]
public sealed class ReviewFormsController : ApiControllerBase
{
    /// <summary>Lists a review cycle's forms.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReviewFormDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] Guid reviewCycleId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListReviewFormsQuery(reviewCycleId), ct));

    /// <summary>Creates a review form with its questions.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateReviewFormRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateReviewFormCommand(request.ReviewCycleId, request.Title, request.Questions), ct));

    /// <summary>Publishes a review form (Draft -> Published, terminal for its question structure).</summary>
    [HttpPost(ApiRoutes.IdParam + "/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new PublishReviewFormCommand(id), ct));
}
