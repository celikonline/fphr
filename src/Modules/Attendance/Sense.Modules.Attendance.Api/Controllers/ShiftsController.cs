using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record UpdateShiftSettingsRequest(
    int MaxDailyMinutes, int MaxWeeklyDays, int MaxWeeklyMinutes, bool NightWorkEnabled, TimeOnly? NightWorkStart, TimeOnly? NightWorkEnd,
    int? NightWorkMaxMinutes, int BreakMinMinutes, bool BreakIsPaid, bool AutoLockEnabled, int AutoLockDayOfMonth, int AutoLockNotifyDaysBefore);

public sealed record AssignShiftRequest(Guid EmployeeId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, bool IsNight, string? Note);

public sealed record RepeatShiftAssignmentsRequest(DateOnly SourceWeekStart, IReadOnlyList<DateOnly> TargetWeekStarts, IReadOnlyList<Guid> EmployeeIds);

public sealed record PublishShiftAssignmentsRequest(IReadOnlyList<Guid> AssignmentIds);

public sealed record UnlockShiftPeriodRequest(string Reason);

/// <summary>Vardiya yönetimi (11.3.6, package A4): ayarlar, haftalık/aylık planlama, yayınlama, plan kilidi.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/shifts")]
[Authorize]
public sealed class ShiftsController : ApiControllerBase
{
    [HttpGet("settings")]
    [ProducesResponseType<ShiftSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetShiftSettingsQuery(), ct));

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateShiftSettingsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateShiftSettingsCommand(
                request.MaxDailyMinutes, request.MaxWeeklyDays, request.MaxWeeklyMinutes, request.NightWorkEnabled, request.NightWorkStart, request.NightWorkEnd,
                request.NightWorkMaxMinutes, request.BreakMinMinutes, request.BreakIsPaid, request.AutoLockEnabled, request.AutoLockDayOfMonth,
                request.AutoLockNotifyDaysBefore),
            ct));

    /// <summary>Haftalık/aylık görünüm; employeeIds boşsa tüm çalışanlar.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ShiftAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] IReadOnlyList<Guid>? employeeIds, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListShiftAssignmentsQuery(from, to, employeeIds), ct));

    [HttpGet("unpublished-count")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnpublishedCount([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetUnpublishedShiftCountQuery(from, to), ct));

    /// <summary>Haftalık görünümde çalışana vardiya gir (Not dahil); aynı çalışan+gün için upsert.</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign([FromBody] AssignShiftRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AssignShiftCommand(request.EmployeeId, request.Date, request.Start, request.End, request.BreakMinutes, request.IsNight, request.Note), ct));

    /// <summary>"Tekrarla".</summary>
    [HttpPost("repeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Repeat([FromBody] RepeatShiftAssignmentsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RepeatShiftAssignmentsCommand(request.SourceWeekStart, request.TargetWeekStarts, request.EmployeeIds), ct));

    /// <summary>"Yayınla".</summary>
    [HttpPost("publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish([FromBody] PublishShiftAssignmentsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new PublishShiftAssignmentsCommand(request.AssignmentIds), ct));

    [HttpGet("periods/{periodMonth}/lock")]
    [ProducesResponseType<ShiftPlanLockStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLockStatus(DateOnly periodMonth, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetShiftPlanLockStatusQuery(periodMonth), ct));

    [HttpPost("periods/{periodMonth}/lock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Lock(DateOnly periodMonth, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new LockShiftPeriodCommand(periodMonth), ct));

    /// <summary>"attendance.shift.unlock": istisnai açma.</summary>
    [HttpPost("periods/{periodMonth}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unlock(DateOnly periodMonth, [FromBody] UnlockShiftPeriodRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UnlockShiftPeriodCommand(periodMonth, request.Reason), ct));

    /// <summary>"Vardiyam" (package A5 self-servis).</summary>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<ShiftAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyShiftAssignmentsQuery(from, to), ct));
}
