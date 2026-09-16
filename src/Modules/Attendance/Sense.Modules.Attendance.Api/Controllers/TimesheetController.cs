using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record RecordAttendanceRequest(DateOnly Date, TimeOnly? CheckIn, TimeOnly? CheckOut);

public sealed record SetWorkedHoursRequest(int WorkedMinutes, string? Reason);

public sealed record AddOvertimeFromAnomalyRequest(TimeOnly StartTime, int DurationMinutes, string? Description);

public sealed record CreateLeaveRequestFromAnomalyRequest(Guid LeaveTypeId, string? Description);

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

    /// <summary>Haftalık ızgara başlığı (11.3.6 "28 Ekim haftası"): o haftanın tamamlanma durumu.</summary>
    [HttpGet("employees/{employeeId:guid}/week-summary")]
    [ProducesResponseType<TimesheetWeekSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> WeekSummary(Guid employeeId, [FromQuery] DateOnly weekStart, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetTimesheetWeekSummaryQuery(employeeId, weekStart), ct));

    [HttpPost("employees/{employeeId:guid}/record")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Record(Guid employeeId, [FromBody] RecordAttendanceRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecordAttendanceCommand(employeeId, request.Date, request.CheckIn, request.CheckOut), ct));

    /// <summary>"Puantajım".</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<AttendanceDayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyAttendanceDaysQuery(from, to), ct));

    /// <summary>"Puantajım" özet kartları (toplam çalışma günü/saati).</summary>
    [HttpGet("me/summary")]
    [ProducesResponseType<MyTimesheetSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MySummary([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyTimesheetSummaryQuery(from, to), ct));

    /// <summary>Ekip haftalık puantaj ızgarası (11.3.6): yönetici, veri kapsamındaki çalışanların bir haftasını görür (isteğe bağlı birim filtresi).</summary>
    [HttpGet("team")]
    [ProducesResponseType<IReadOnlyList<TeamWeekRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Team([FromQuery] DateOnly weekStart, [FromQuery] Guid? orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetTeamWeekQuery(weekStart, orgUnitId), ct));

    /// <summary>Hücre çekmecesindeki hareketler (11.3.6): bir günün uyuşmazlık çözüm geçmişi.</summary>
    [HttpGet("days/{attendanceDayId:guid}/resolutions")]
    [ProducesResponseType<IReadOnlyList<AnomalyResolutionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListResolutions(Guid attendanceDayId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAnomalyResolutionsQuery(attendanceDayId), ct));

    /// <summary>"Çalışılan saat" önerisi.</summary>
    [HttpPost("days/{attendanceDayId:guid}/resolve/worked-hours")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetWorkedHours(Guid attendanceDayId, [FromBody] SetWorkedHoursRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetWorkedHoursCommand(attendanceDayId, request.WorkedMinutes, request.Reason), ct));

    /// <summary>"Fazla mesai ekle" önerisi.</summary>
    [HttpPost("days/{attendanceDayId:guid}/resolve/overtime")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddOvertime(Guid attendanceDayId, [FromBody] AddOvertimeFromAnomalyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddOvertimeFromAnomalyCommand(attendanceDayId, request.StartTime, request.DurationMinutes, request.Description), ct));

    /// <summary>"İzin talebi" önerisi.</summary>
    [HttpPost("days/{attendanceDayId:guid}/resolve/leave-request")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateLeaveRequest(Guid attendanceDayId, [FromBody] CreateLeaveRequestFromAnomalyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateLeaveRequestFromAnomalyCommand(attendanceDayId, request.LeaveTypeId, request.Description), ct));

    /// <summary>Çipleri elle temizle (öneri uygulamadan).</summary>
    [HttpPost("days/{attendanceDayId:guid}/resolve/ignore")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ignore(Guid attendanceDayId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new IgnoreAttendanceAnomalyCommand(attendanceDayId), ct));
}
