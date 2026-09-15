using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.OrgUnits;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to create an org unit.</summary>
public sealed record CreateOrgUnitRequest(
    Guid TypeId,
    Guid? ParentId,
    string Name,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    int SortOrder = 0);

/// <summary>Request to update an org unit.</summary>
public sealed record UpdateOrgUnitRequest(
    string Name,
    Guid? ParentId,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    int SortOrder);

/// <summary>Org unit tree management: dynamic hierarchy levels replace a fixed Department level (Faz 0 madde 4).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/units")]
[Authorize]
public sealed class OrgUnitsController : ApiControllerBase
{
    /// <summary>Flat list of org units for the current tenant (active-only by default).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrgUnitDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool? activeOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOrgUnitsQuery(activeOnly ?? true), ct));

    /// <summary>Full org unit tree (nested shape), built server-side from the flat active list.</summary>
    [HttpGet("tree")]
    [ProducesResponseType<IReadOnlyList<OrgUnitTreeNodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tree(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetOrgUnitTreeQuery(), ct));

    /// <summary>Creates an org unit at any hierarchy level.</summary>
    [HttpPost]
    [ProducesResponseType<OrgUnitDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateOrgUnitRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateOrgUnitCommand(request.TypeId, request.ParentId, request.Name, request.ManagerEmployeeId, request.LocationId, request.SortOrder),
            ct));

    /// <summary>Renames/reparents an org unit and (re)assigns its manager/location.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<OrgUnitDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrgUnitRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateOrgUnitCommand(id, request.Name, request.ParentId, request.ManagerEmployeeId, request.LocationId, request.SortOrder),
            ct));

    /// <summary>Soft delete: sets IsActive=false (409 if the unit still has active children).</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateOrgUnitCommand(id), ct));
}
