using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.MyTeam;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

/// <summary>"Ekibim" (FG-007): the caller's direct reports' attendance today (attendance.my.team). Weekly/monthly grids live on TimesheetController (me/team).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/me/team")]
[Authorize]
public sealed class AttendanceMyTeamController : ApiControllerBase
{
    [HttpGet("today")]
    [ProducesResponseType<IReadOnlyList<MyTeamTodayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Today(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyTeamTodayQuery(), ct));
}
