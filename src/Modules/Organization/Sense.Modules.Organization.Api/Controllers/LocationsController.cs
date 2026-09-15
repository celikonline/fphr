using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.Locations;
using Sense.Modules.Organization.Domain.Locations;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to create a location.</summary>
public sealed record CreateLocationRequest(string Name, LocationType Type, string CountryCode, string? Address);

/// <summary>Request to update a location.</summary>
public sealed record UpdateLocationRequest(string Name, LocationType Type, string CountryCode, string? Address);

/// <summary>
/// Physical location management (office/store/warehouse/factory/remote + country, roadmap Faz 0 item #6).
/// Grid standard: ?page&amp;pageSize&amp;sort&amp;q, matching web/src/services/locations.service.ts.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/locations")]
[Authorize]
public sealed class LocationsController : ApiControllerBase
{
    /// <summary>Paged, searchable, sortable list of locations for the current tenant (active-only).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<LocationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListLocationsQuery(paging), ct));

    /// <summary>Creates a location.</summary>
    [HttpPost]
    [ProducesResponseType<LocationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateLocationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateLocationCommand(request.Name, request.Type, request.CountryCode, request.Address), ct));

    /// <summary>Updates a location's name/type/country/address.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<LocationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLocationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateLocationCommand(id, request.Name, request.Type, request.CountryCode, request.Address), ct));

    /// <summary>Soft delete: sets IsActive=false (409 if an active org unit still references this location).</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeactivateLocationCommand(id), ct));
}
