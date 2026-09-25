using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record UpdateShiftSettingsRequest(
    int MaxDailyMinutes, int MaxWeeklyDays, int MaxWeeklyMinutes, bool NightWorkEnabled, TimeOnly? NightWorkStart, TimeOnly? NightWorkEnd,
    int? NightWorkMaxMinutes, int BreakMinMinutes, bool BreakIsPaid, bool AutoLockEnabled, int AutoLockDayOfMonth, int AutoLockNotifyDaysBefore);

public sealed record AssignShiftRequest(Guid EmployeeId, DateOnly Date, TimeOnly Start, TimeOnly End, int BreakMinutes, bool IsNight, string? Note);

public sealed record RepeatShiftAssignmentsRequest(DateOnly SourceWeekStart, IReadOnlyList<DateOnly> TargetWeekStarts, IReadOnlyList<Guid> EmployeeIds);

public sealed record PublishShiftAssignmentsRequest(IReadOnlyList<Guid> AssignmentIds);

public sealed record UnlockShiftPeriodRequest(string Reason);

public sealed record BulkAssignShiftsRequest(
    IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<DateOnly> Dates, Guid? ShiftDefinitionId, ShiftDayType DayType, bool OverwritePublished = false);

public sealed record BulkClearShiftsRequest(IReadOnlyList<Guid> EmployeeIds, IReadOnlyList<DateOnly> Dates, bool OverwritePublished = false);

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

    /// <summary>Toplu atama (çalışan x gün): work için shiftDefinitionId zorunlu, diğer gün türlerinde yok sayılır; kilitli/yayınlanmış hücreler skipped[] ile döner.</summary>
    [HttpPost("bulk-assign")]
    [ProducesResponseType<ShiftBulkResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignShiftsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new BulkAssignShiftsCommand(request.EmployeeIds, request.Dates, request.ShiftDefinitionId, request.DayType, request.OverwritePublished), ct));

    [HttpPost("bulk-clear")]
    [ProducesResponseType<ShiftBulkResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BulkClear([FromBody] BulkClearShiftsRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new BulkClearShiftsCommand(request.EmployeeIds, request.Dates, request.OverwritePublished), ct));

    /// <summary>Aralıkta hiç ataması olmayan çalışan id'leri.</summary>
    [HttpGet("unassigned")]
    [ProducesResponseType<IReadOnlyList<Guid>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unassigned([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] IReadOnlyList<Guid>? employeeIds, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListUnassignedEmployeesQuery(from, to, employeeIds), ct));

    [HttpGet("totals")]
    [ProducesResponseType<ShiftPlanTotalsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Totals([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] IReadOnlyList<Guid>? employeeIds, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetShiftPlanTotalsQuery(from, to, employeeIds), ct));
}
