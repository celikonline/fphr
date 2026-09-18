using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record CreateWorkScheduleRequest(string Name, IReadOnlyList<WorkScheduleDayDto> Days, bool IncludeSaturdayWhenFullWeek);

public sealed record RenameWorkScheduleRequest(string Name);

public sealed record UpdateWorkScheduleDaysRequest(IReadOnlyList<WorkScheduleDayDto> Days, bool IncludeSaturdayWhenFullWeek);

public sealed record SetWorkScheduleActiveRequest(bool IsActive);

public sealed record AssignWorkScheduleRequest(IReadOnlyList<Guid> EmployeeIds, DateOnly EffectiveFrom);

/// <summary>Ayarlar &gt; Kurallar &gt; Çalışma takvimleri (11.3.6).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/work-schedules")]
[Authorize]
public sealed class WorkSchedulesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkScheduleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool includeInactive, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new ListWorkSchedulesQuery(includeInactive), ct)).Localize(localizer));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<WorkScheduleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetWorkScheduleQuery(id), ct)).Localize(localizer));

    /// <summary>"Yeni çalışma takvimi ekle".</summary>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateWorkScheduleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateWorkScheduleCommand(request.Name, request.Days, request.IncludeSaturdayWhenFullWeek), ct));

    [HttpPut(ApiRoutes.IdParam + "/name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameWorkScheduleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RenameWorkScheduleCommand(id, request.Name), ct));

    [HttpPut(ApiRoutes.IdParam + "/days")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateDays(Guid id, [FromBody] UpdateWorkScheduleDaysRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateWorkScheduleDaysCommand(id, request.Days, request.IncludeSaturdayWhenFullWeek), ct));

    [HttpPost(ApiRoutes.IdParam + "/set-default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetDefaultWorkScheduleCommand(id), ct));

    [HttpPut(ApiRoutes.IdParam + "/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetWorkScheduleActiveRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetWorkScheduleActiveCommand(id, request.IsActive), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteWorkScheduleCommand(id), ct));

    /// <summary>Çalışanlar &gt; Toplu işlemler &gt; Çalışma takvimi ekle (also the single-employee card assignment).</summary>
    [HttpPost(ApiRoutes.IdParam + "/assign")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignWorkScheduleRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AssignWorkScheduleCommand(id, request.EmployeeIds, request.EffectiveFrom), ct));

    /// <summary>Çalışan kartı "Kariyer &gt; Çalışma Takvimi": dated assignment history (HR/yönetici tarafı).</summary>
    [HttpGet("employees/{employeeId:guid}/assignments")]
    [ProducesResponseType<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAssignments(Guid employeeId, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new ListEmployeeWorkScheduleAssignmentsQuery(employeeId), ct)).LocalizeAssignments(localizer));

    /// <summary>Çalışan kartı "Kariyer &gt; Çalışma Takvimi": own card (self-service).</summary>
    [HttpGet("me/assignments")]
    [ProducesResponseType<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyAssignments([FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetMyWorkScheduleAssignmentsQuery(), ct)).LocalizeAssignments(localizer));
}

/// <summary>The seeded default schedule stores a resource key as its name ("attendance.work_schedule.default.name"); the API returns it translated.</summary>
internal static class WorkScheduleTextLocalizer
{
    public static Result<IReadOnlyList<WorkScheduleDto>> Localize(this Result<IReadOnlyList<WorkScheduleDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<WorkScheduleDto>>(result.Value.Select(s => s with { Name = localizer.MessageOrText(s.Name) }).ToList());

    public static Result<WorkScheduleDto> Localize(this Result<WorkScheduleDto> result, IErrorLocalizer localizer) =>
        result.IsFailure ? result : result.Value with { Name = localizer.MessageOrText(result.Value.Name) };

    public static Result<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>> LocalizeAssignments(
        this Result<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<EmployeeWorkScheduleAssignmentDto>>(
                result.Value.Select(a => a with { WorkScheduleName = localizer.MessageOrText(a.WorkScheduleName) }).ToList());
}
