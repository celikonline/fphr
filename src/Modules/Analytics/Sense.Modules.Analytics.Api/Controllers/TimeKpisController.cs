using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Analytics.Api.Controllers;

/// <summary>Zaman ve devam KPI'ları (zaman-devam Z1.3); izin <c>attendance.dashboard.view</c>, kapsam IDataScopeService.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/analytics/kpis")]
[Authorize]
public sealed class TimeKpisController : ApiControllerBase
{
    [HttpGet("attendance-7d")]
    [ProducesResponseType<IReadOnlyList<DailyAttendanceTotalsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Attendance7d([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAttendanceTrendKpiQuery(from, to), ct));

    [HttpGet("late-early-by-unit")]
    [ProducesResponseType<IReadOnlyList<LateEarlyByUnitDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LateEarlyByUnit([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetLateEarlyByUnitKpiQuery(from, to), ct));

    [HttpGet("overtime-by-group")]
    [ProducesResponseType<IReadOnlyList<OvertimeByGroupDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OvertimeByGroup(
        [FromQuery] string groupBy, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetOvertimeByGroupKpiQuery(groupBy, from, to), ct));

    [HttpGet("shift-attendance-rate")]
    [ProducesResponseType<ShiftAttendanceRateDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ShiftAttendanceRate([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetShiftAttendanceRateKpiQuery(from, to), ct));
}
