using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Assets.Api.Controllers;

public sealed record CreateAssetRequest(Guid CategoryId, string Name, string? SerialNumber, string? Description);

public sealed record UpdateAssetRequest(string Name, string? SerialNumber, string? Description);

public sealed record AssignAssetRequest(Guid EmployeeId, DateOnly IssuedAt);

public sealed record ReturnAssetRequest(DateOnly ReturnedAt);

/// <summary>Company equipment ("zimmet") assigned to employees: category, serial number, issue/return dates.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/assets")]
[Authorize]
public sealed class AssetsController : ApiControllerBase
{
    /// <summary>Lists assets, optionally filtered by employee/status/category (HR/manager view).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId, [FromQuery] AssetStatus? status, [FromQuery] Guid? categoryId, [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAssetsQuery(employeeId, status, categoryId, paging), ct));

    /// <summary>Self-service: the equipment currently assigned to the caller's own employee record.</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyAssetsQuery(), ct));

    /// <summary>The employee card's "Zimmet" tab (HR/manager view).</summary>
    [HttpGet("employees/{employeeId:guid}")]
    [ProducesResponseType<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForEmployee(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeAssetsQuery(employeeId), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAssetQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateAssetCommand(request.CategoryId, request.Name, request.SerialNumber, request.Description), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateAssetCommand(id, request.Name, request.SerialNumber, request.Description), ct));

    /// <summary>Assigns an Unassigned asset to an employee.</summary>
    [HttpPost(ApiRoutes.IdParam + "/assign")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AssignAssetCommand(id, request.EmployeeId, request.IssuedAt), ct));

    /// <summary>Returns an Assigned asset (back to Unassigned).</summary>
    [HttpPost(ApiRoutes.IdParam + "/return")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReturnAssetCommand(id, request.ReturnedAt), ct));

    /// <summary>Retires an asset (must be returned first if currently assigned).</summary>
    [HttpPost(ApiRoutes.IdParam + "/retire")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RetireAssetCommand(id), ct));
}
