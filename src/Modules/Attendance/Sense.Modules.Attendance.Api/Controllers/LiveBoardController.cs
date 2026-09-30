using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>Canlı içeride/dışarıda panosu (S-31 kabul kriteri 3): polling ile yenilenen, o anki giriş/çıkış durumu (attendance.board.view).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/board")]
[Authorize]
public sealed class LiveBoardController : ApiControllerBase
{
    [HttpGet("live")]
    [ProducesResponseType<IReadOnlyList<LiveAttendanceStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Live([FromQuery] Guid? orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetLiveAttendanceBoardQuery(orgUnitId), ct));

    /// <summary>Canlı monitör terminal durumu (Z5.3): çevrimiçi/çevrimdışı, son okutma, bugünkü okutma sayısı (attendance.terminal.view).</summary>
    [HttpGet("terminals")]
    [ProducesResponseType<IReadOnlyList<LiveTerminalStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Terminals(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetLiveTerminalStatusQuery(), ct));
}
