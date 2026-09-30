using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Daily;
using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record AddDailyPunchRequest(Guid EmployeeId, DateTime OccurredAtUtc, PunchDirectionDto Direction, string Reason);

public sealed record DeleteDailyPunchRequest(string Reason);

/// <summary>"Günlük Puantaj" (zaman-devam Z1.1): geldi/geç/erken/devamsız/fazla/az/izinli listeleri ve gerekçeli elle okutma ekle/sil.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/daily")]
[Authorize]
public sealed class DailyAttendanceController : ApiControllerBase
{
    private const int DefaultPageSize = 50;

    [HttpGet]
    [ProducesResponseType<PagedResult<DailyAttendanceRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly date, [FromQuery] DailyStatus status, [FromQuery] Guid? orgUnitId, [FromQuery] bool includeSubUnits = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListDailyAttendanceQuery(date, status, orgUnitId, includeSubUnits, page, pageSize), ct));

    [HttpGet("counts")]
    [ProducesResponseType<IReadOnlyDictionary<string, int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Counts(
        [FromQuery] DateOnly date, [FromQuery] Guid? orgUnitId, [FromQuery] bool includeSubUnits = false, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new GetDailyAttendanceCountsQuery(date, orgUnitId, includeSubUnits), ct));

    [HttpPost("punches")]
    [ProducesResponseType<DailyPunchCreatedDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPunch([FromBody] AddDailyPunchRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddDailyPunchCommand(request.EmployeeId, request.OccurredAtUtc, request.Direction, request.Reason), ct));

    [HttpDelete("punches/{id:guid}")]
    public async Task<IActionResult> DeletePunch(Guid id, [FromBody] DeleteDailyPunchRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteDailyPunchCommand(id, request.Reason), ct));
}
