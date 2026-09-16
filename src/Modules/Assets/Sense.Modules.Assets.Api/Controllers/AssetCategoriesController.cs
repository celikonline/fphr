using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Assets.Application.Categories;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Assets.Api.Controllers;

public sealed record CreateAssetCategoryRequest(string Code, string Name);

public sealed record UpdateAssetCategoryRequest(string Name);

/// <summary>Equipment category catalog (seeded per tenant: Computer/Phone/Vehicle/Other). No admin management UI in this pass (deferred gap).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/assets/categories")]
[Authorize]
public sealed class AssetCategoriesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AssetCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAssetCategoriesQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAssetCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateAssetCategoryCommand(request.Code, request.Name), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAssetCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateAssetCategoryCommand(id, request.Name), ct));
}
