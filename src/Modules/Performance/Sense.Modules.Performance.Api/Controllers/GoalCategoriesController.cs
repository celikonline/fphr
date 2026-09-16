using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Performance.Api.Controllers;

public sealed record CreateGoalCategoryRequest(string Name);

/// <summary>Kiracı tanımlı hedef kategorileri (Hedef ekleme diyaloğundaki seçim listesi).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/performance/goal-categories")]
[Authorize]
public sealed class GoalCategoriesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GoalCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => FromResult(await Dispatcher.Query(new ListGoalCategoriesQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateGoalCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateGoalCategoryCommand(request.Name), ct));
}
