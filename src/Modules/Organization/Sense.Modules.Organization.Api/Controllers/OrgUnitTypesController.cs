using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.OrgUnitTypes;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to add a tenant-custom level right after an active level (null = at the end).</summary>
public sealed record CreateOrgUnitTypeRequest(string Name, Guid? InsertAfterTypeId);

/// <summary>Request to re-activate an inactive level at a position (null = at the end).</summary>
public sealed record ActivateOrgUnitTypeRequest(Guid? InsertAfterTypeId);

/// <summary>Request to rename a level.</summary>
public sealed record RenameOrgUnitTypeRequest(string Name);

/// <summary>Request to reorder the active levels (Company must stay first).</summary>
public sealed record ReorderOrgUnitTypesRequest(IReadOnlyList<Guid> OrderedTypeIds);

/// <summary>Org hierarchy levels (Company/Branch/Department/Title/...): list, add ("+"), activate, rename, reorder, remove.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/unit-types")]
[Authorize]
public sealed class OrgUnitTypesController : ApiControllerBase
{
    /// <summary>Lists hierarchy levels for the current tenant, ordered by SortOrder.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrgUnitTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool activeOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOrgUnitTypesQuery(activeOnly), ct));

    /// <summary>Adds a tenant-custom level; active levels are renumbered 1..n.</summary>
    [HttpPost]
    [ProducesResponseType<OrgUnitTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateOrgUnitTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateOrgUnitTypeCommand(request.Name, request.InsertAfterTypeId), ct));

    /// <summary>Re-activates an inactive level at the given position.</summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType<OrgUnitTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, [FromBody] ActivateOrgUnitTypeRequest? request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ActivateOrgUnitTypeCommand(id, request?.InsertAfterTypeId), ct));

    /// <summary>Reorders the active levels; returns all levels.</summary>
    [HttpPut("order")]
    [ProducesResponseType<IReadOnlyList<OrgUnitTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reorder([FromBody] ReorderOrgUnitTypesRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReorderOrgUnitTypesCommand(request.OrderedTypeIds ?? []), ct));

    /// <summary>Renames a level.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<OrgUnitTypeDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameOrgUnitTypeRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RenameOrgUnitTypeCommand(id, request.Name), ct));

    /// <summary>Removes a level: 422 for Company, 409 while active employees are assigned to its units, otherwise deactivates it with its units.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteOrgUnitTypeCommand(id), ct));
}
