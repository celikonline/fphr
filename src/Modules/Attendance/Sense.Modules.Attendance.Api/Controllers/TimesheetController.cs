using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record RecordAttendanceRequest(DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut);

/// <summary>Puantaj (11.3.6): haftalık ızgara verisi + "Çalışılan saat" manuel kaydı/düzeltmesi.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/timesheet")]
[Authorize]
public sealed class TimesheetController : ApiControllerBase
{
    [HttpGet("employees/{employeeId:guid}")]
    [ProducesResponseType<IReadOnlyList<AttendanceDayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAttendanceDaysQuery(employeeId, from, to), ct));

    [HttpPost("employees/{employeeId:guid}/record")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Record(Guid employeeId, [FromBody] RecordAttendanceRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecordAttendanceCommand(employeeId, request.Date, request.CheckIn, request.CheckOut), ct));

    /// <summary>"Puantajım".</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<AttendanceDayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyAttendanceDaysQuery(from, to), ct));
}
