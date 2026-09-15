using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.OrgUnitTypes;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Org hierarchy levels (Company/Branch/Department/Title/...). Read-only endpoint in Faz 0; levels are seeded per tenant.</summary>
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
}
