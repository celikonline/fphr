using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Analytics.Api.Controllers;

/// <summary>Company-wide workforce KPIs (roadmap Faz 7 #99).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/analytics/kpis")]
[Authorize]
public sealed class WorkforceKpisController : ApiControllerBase
{
    [HttpGet("workforce")]
    [ProducesResponseType<IReadOnlyList<KpiResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Workforce([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetWorkforceKpisQuery(from, to), ct));
}
