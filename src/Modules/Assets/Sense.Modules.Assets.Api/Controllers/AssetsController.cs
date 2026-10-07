using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Assets.Application.Assets;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Assets.Api.Controllers;

public sealed record CreateAssetRequest(
    Guid CategoryId, string Name, string? SerialNumber, string? Description,
    string? AssetTag, string? Brand, string? Model, DateOnly? PurchaseDate, decimal? PurchaseCost, DateOnly? WarrantyEndsOn);

public sealed record UpdateAssetRequest(
    string Name, string? SerialNumber, string? Description,
    string? AssetTag, string? Brand, string? Model, DateOnly? PurchaseDate, decimal? PurchaseCost, DateOnly? WarrantyEndsOn,
    AssetCondition? Condition);

public sealed record AssignAssetRequest(Guid EmployeeId, DateOnly IssuedAt);

public sealed record ReturnAssetRequest(DateOnly ReturnedAt, AssetCondition Condition = AssetCondition.Good, string? Note = null);

public sealed record MarkAssetLostRequest(DateOnly OccurredOn, string? Note);

/// <summary>Company equipment ("zimmet") assigned to employees: category, künye, assignment ledger, issue/return/loss, receipt acknowledgement.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/assets")]
[Authorize]
public sealed class AssetsController : ApiControllerBase
{
    /// <summary>Lists assets, optionally filtered by employee/status/category and a free-text search (HR/manager view).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? employeeId, [FromQuery] AssetStatus? status, [FromQuery] Guid? categoryId, [FromQuery] string? search,
        [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAssetsQuery(employeeId, status, categoryId, paging, search), ct));

    /// <summary>Inventory board counts (status, unacknowledged, warranty, KKD replacement, per category).</summary>
    [HttpGet("summary")]
    [ProducesResponseType<AssetSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAssetSummaryQuery(), ct));

    /// <summary>Self-service: the equipment currently assigned to the caller's own employee record.</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyAssetsQuery(), ct));

    /// <summary>Self-service: every asset the caller ever held (open and closed), newest first.</summary>
    [HttpGet("my/history")]
    [ProducesResponseType<IReadOnlyList<EmployeeAssetHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyHistory(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyAssetHistoryQuery(), ct));

    /// <summary>The employee card's "Zimmet" tab (HR/manager view).</summary>
    [HttpGet("employees/{employeeId:guid}")]
    [ProducesResponseType<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListForEmployee(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeAssetsQuery(employeeId), ct));

    /// <summary>Every asset the employee ever held (open and closed), newest first (HR/manager view).</summary>
    [HttpGet("employees/{employeeId:guid}/history")]
    [ProducesResponseType<IReadOnlyList<EmployeeAssetHistoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EmployeeHistory(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeAssetHistoryQuery(employeeId), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<AssetDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAssetQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateAssetCommand(
                request.CategoryId, request.Name, request.SerialNumber, request.Description,
                request.AssetTag, request.Brand, request.Model, request.PurchaseDate, request.PurchaseCost, request.WarrantyEndsOn), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateAssetCommand(
                id, request.Name, request.SerialNumber, request.Description,
                request.AssetTag, request.Brand, request.Model, request.PurchaseDate, request.PurchaseCost, request.WarrantyEndsOn, request.Condition), ct));

    /// <summary>Assigns an Unassigned asset to an employee.</summary>
    [HttpPost(ApiRoutes.IdParam + "/assign")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AssignAssetCommand(id, request.EmployeeId, request.IssuedAt), ct));

    /// <summary>Returns an Assigned asset (back to Unassigned), recording its condition and an optional note.</summary>
    [HttpPost(ApiRoutes.IdParam + "/return")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Return(Guid id, [FromBody] ReturnAssetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReturnAssetCommand(id, request.ReturnedAt, request.Condition, request.Note), ct));

    /// <summary>Reports an Unassigned or Assigned asset lost (an open assignment closes with reason "lost").</summary>
    [HttpPost(ApiRoutes.IdParam + "/lost")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkLost(Guid id, [FromBody] MarkAssetLostRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkAssetLostCommand(id, request.OccurredOn, request.Note), ct));

    /// <summary>Self-service "teslim aldım": the assignee confirms receipt of the asset.</summary>
    [HttpPost(ApiRoutes.IdParam + "/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AcknowledgeAssetCommand(id), ct));

    /// <summary>The asset's assignment ledger, newest first.</summary>
    [HttpGet(ApiRoutes.IdParam + "/history")]
    [ProducesResponseType<IReadOnlyList<AssetAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAssetHistoryQuery(id), ct));

    /// <summary>Retires an asset (must be returned first if currently assigned).</summary>
    [HttpPost(ApiRoutes.IdParam + "/retire")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Retire(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RetireAssetCommand(id), ct));

    /// <summary>Zimmet tutanağı PDF; 404 when the asset doesn't exist or isn't currently assigned.</summary>
    [HttpGet(ApiRoutes.IdParam + "/handover-form")]
    public async Task<IActionResult> HandoverForm(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetAssetHandoverFormQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }
}
