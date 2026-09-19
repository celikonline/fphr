using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

/// <summary>Self-servis: "Bana atanan formlar" (/app/performance).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/my-assignments")]
[Authorize]
public sealed class MyAssignmentsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MyAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListMyAssignmentsQuery(), ct));

    /// <summary>"Hedeflerim" (yalnız Kind=CompetencyAndGoal, IncludeGoals açık dönemler).</summary>
    [HttpGet("/api/v{version:apiVersion}/performance/my-goals")]
    [ProducesResponseType<IReadOnlyList<MyGoalCycleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGoals(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListMyGoalsQuery(), ct));

    /// <summary>"Sonuçlarım": kendi katılımcı kayıtlarım (ilerleme + görünürse puan).</summary>
    [HttpGet("/api/v{version:apiVersion}/performance/my-results")]
    [ProducesResponseType<IReadOnlyList<MyReviewResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListResults(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListMyReviewResultsQuery(), ct));
}
