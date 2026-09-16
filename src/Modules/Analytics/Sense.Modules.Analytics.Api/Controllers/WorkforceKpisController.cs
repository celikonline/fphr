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

    /// <summary>S-15 izin kullanım KPI'sı (roadmap #99).</summary>
    [HttpGet("leave-usage")]
    [ProducesResponseType<LeaveUsageKpiDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LeaveUsage([FromQuery] int? year, [FromQuery] Guid? orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetLeaveUsageKpiQuery(year, orgUnitId), ct));

    /// <summary>S-17 fazla mesai maliyeti KPI'sı (roadmap #99); maaş hassas alan - <c>employee.salary.view</c> de gerekir.</summary>
    [HttpGet("overtime-cost")]
    [ProducesResponseType<OvertimeCostKpiDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OvertimeCost([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetOvertimeCostKpiQuery(year, month, orgUnitId), ct));
}
