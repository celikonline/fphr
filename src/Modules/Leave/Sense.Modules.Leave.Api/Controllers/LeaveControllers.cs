using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

public sealed record CloneLeaveRuleSetRequest(string Name);

public sealed record RenameLeaveRuleSetRequest(string Name);

public sealed record AssignLeaveRuleSetRequest(IReadOnlyList<Guid> EmployeeIds);

public sealed record LeaveRuleSetAssignmentWarningResponse(Guid EmployeeId, string Code, string Message);

public sealed record LeaveRuleSetAssignmentResponse(int AssignedCount, IReadOnlyList<LeaveRuleSetAssignmentWarningResponse> Warnings);

public sealed record SetLeaveTypeDisabledRequest(bool Disabled);

/// <summary>Self-service submit/edit body; <c>PreviousRequestId</c> (submit only) re-submits the caller's rejected request.</summary>
public sealed record LeaveRequestBody(Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, Guid? PreviousRequestId = null);

public sealed record CancelLeaveRequestBody(string Reason);

public sealed record LeaveRequestOnBehalfBody(Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description, bool BypassApproval);

public sealed record AdjustLeaveBalanceRequest(Guid LeaveTypeId, decimal Days, DateOnly EffectiveDate, string Reason);

/// <summary>İzinler → Kurallar: rule sets and their leave types.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/rule-sets")]
[Authorize]
public sealed class LeaveRuleSetsController : ApiControllerBase
{
    private const string TypeRoute = ApiRoutes.IdParam + "/types/{typeId:guid}";

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LeaveRuleSetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListLeaveRuleSetsQuery(), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<LeaveRuleSetDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetLeaveRuleSetQuery(id), ct));

    /// <summary>"Kopyala ve düzenle".</summary>
    [HttpPost(ApiRoutes.IdParam + "/clone")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Clone(Guid id, [FromBody] CloneLeaveRuleSetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloneLeaveRuleSetCommand(id, request.Name), ct));

    [HttpPut(ApiRoutes.IdParam + "/name")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameLeaveRuleSetRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RenameLeaveRuleSetCommand(id, request.Name), ct));

    [HttpPost(ApiRoutes.IdParam + "/set-default")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetDefaultLeaveRuleSetCommand(id), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteLeaveRuleSetCommand(id), ct));

    /// <summary>Assigns the rule set to employees and recalculates their balances; warnings are non-blocking.</summary>
    [HttpPost(ApiRoutes.IdParam + "/assign")]
    [ProducesResponseType<LeaveRuleSetAssignmentResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignLeaveRuleSetRequest request, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Send(new AssignLeaveRuleSetCommand(id, request.EmployeeIds ?? []), ct)).Localize(localizer));

    [HttpPost(ApiRoutes.IdParam + "/types")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddType(Guid id, [FromBody] LeaveTypeSettings request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AddLeaveTypeCommand(id, request), ct));

    [HttpPut(TypeRoute)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateType(Guid id, Guid typeId, [FromBody] LeaveTypeSettings request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateLeaveTypeCommand(id, typeId, request), ct));

    [HttpPut(TypeRoute + "/disabled")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetTypeDisabled(Guid id, Guid typeId, [FromBody] SetLeaveTypeDisabledRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetLeaveTypeDisabledCommand(id, typeId, request.Disabled), ct));
}

/// <summary>Seeded rule sets/types store resource keys as names ("leave.type.annual"); the API returns them translated.</summary>
internal static class LeaveTextLocalizer
{
    public static Result<IReadOnlyList<LeaveRequestDto>> Localize(this Result<IReadOnlyList<LeaveRequestDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<LeaveRequestDto>>(
                result.Value.Select(r => r with { LeaveTypeName = r.LeaveTypeName is null ? null : localizer.MessageOrText(r.LeaveTypeName) }).ToList());

    public static Result<LeaveRequestDto> Localize(this Result<LeaveRequestDto> result, IErrorLocalizer localizer) =>
        result.IsFailure || result.Value.LeaveTypeName is null
            ? result
            : result.Value with { LeaveTypeName = localizer.MessageOrText(result.Value.LeaveTypeName) };

    public static Result<IReadOnlyList<RequestableLeaveTypeDto>> Localize(this Result<IReadOnlyList<RequestableLeaveTypeDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<RequestableLeaveTypeDto>>(result.Value.Select(t => t with { Name = localizer.MessageOrText(t.Name) }).ToList());

    public static Result<LeaveRequestDetailDto> Localize(this Result<LeaveRequestDetailDto> result, IErrorLocalizer localizer) =>
        result.IsFailure || result.Value.LeaveTypeName is null
            ? result
            : result.Value with { LeaveTypeName = localizer.MessageOrText(result.Value.LeaveTypeName) };

    public static Result<IReadOnlyList<LeaveCalendarEntryDto>> Localize(this Result<IReadOnlyList<LeaveCalendarEntryDto>> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result
            : Result.Success<IReadOnlyList<LeaveCalendarEntryDto>>(
                result.Value.Select(e => e with { LeaveTypeName = e.LeaveTypeName is null ? null : localizer.MessageOrText(e.LeaveTypeName) }).ToList());

    public static Result<LeaveRuleSetAssignmentResponse> Localize(this Result<LeaveRuleSetAssignmentResult> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result.Error
            : new LeaveRuleSetAssignmentResponse(
                result.Value.AssignedCount,
                result.Value.Warnings
                    .Select(w => new LeaveRuleSetAssignmentWarningResponse(
                        w.EmployeeId, w.Code, localizer.Message(Error.Validation(w.Code, ("leaveType", localizer.MessageOrText(w.LeaveTypeName))))))
                    .ToList());

    /// <summary>Warnings stay codes (web can branch on them) but the message is localized alongside.</summary>
    public static Result<LeaveRequestPreviewResponse> Localize(this Result<LeaveRequestPreviewDto> result, IErrorLocalizer localizer) =>
        result.IsFailure
            ? result.Error
            : new LeaveRequestPreviewResponse(
                result.Value.Days, result.Value.WeeklyRestDays, result.Value.PublicHolidayDays,
                result.Value.Warnings.Select(error => new LeavePreviewWarning(error.Code, localizer.Message(error))).ToList());
}

public sealed record LeavePreviewWarning(string Code, string Message);

public sealed record LeaveRequestPreviewResponse(decimal Days, int WeeklyRestDays, int PublicHolidayDays, IReadOnlyList<LeavePreviewWarning> Warnings);

/// <summary>Employee leave balances and ledger (çalışan kartı → İzin).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/employees/{employeeId:guid}")]
[Authorize]
public sealed class LeaveEmployeeBalancesController : ApiControllerBase
{
    [HttpGet("balances")]
    [ProducesResponseType<EmployeeLeaveBalancesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Balances(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeLeaveBalancesQuery(employeeId), ct));

    [HttpGet("requests")]
    [ProducesResponseType<IReadOnlyList<LeaveRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests(Guid employeeId, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new ListEmployeeLeaveRequestsQuery(employeeId), ct)).Localize(localizer));

    [HttpGet("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType<LeaveRequestDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestDetail(Guid employeeId, Guid id, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetEmployeeLeaveRequestQuery(employeeId, id), ct)).Localize(localizer));

    /// <summary>Cancels a pending or approved leave (decision #16: approved leave is cancelled, never edited).</summary>
    [HttpPost("requests/" + ApiRoutes.IdParam + "/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid employeeId, Guid id, [FromBody] CancelLeaveRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CancelLeaveRequestCommand(employeeId, id, request.Reason), ct));

    /// <summary>İşlemler → İzin Ekle.</summary>
    [HttpPost("requests")]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateOnBehalf(
        Guid employeeId, [FromBody] LeaveRequestOnBehalfBody request, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Send(
            new CreateLeaveRequestOnBehalfCommand(employeeId, request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.BypassApproval), ct))
            .Localize(localizer));

    [HttpGet("requests/preview")]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        Guid employeeId, [FromQuery] Guid leaveTypeId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new PreviewEmployeeLeaveRequestQuery(employeeId, leaveTypeId, start, end), ct)).Localize(localizer));

    [HttpGet("ledger")]
    [ProducesResponseType<IReadOnlyList<LeaveLedgerEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ledger(Guid employeeId, [FromQuery] Guid? leaveTypeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListLeaveLedgerQuery(employeeId, leaveTypeId), ct));

    [HttpPost("adjustments")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjust(Guid employeeId, [FromBody] AdjustLeaveBalanceRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AdjustLeaveBalanceCommand(employeeId, request.LeaveTypeId, request.Days, request.EffectiveDate, request.Reason), ct));

    [HttpPost("recalculate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Recalculate(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecalculateLeaveBalancesCommand(employeeId), ct));
}

/// <summary>Self-service leave ("Kendi izinlerim").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/me")]
[Authorize]
public sealed class MyLeaveController : ApiControllerBase
{
    [HttpGet("balances")]
    [ProducesResponseType<EmployeeLeaveBalancesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Balances(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetMyLeaveBalancesQuery(), ct));

    [HttpGet("ledger")]
    [ProducesResponseType<IReadOnlyList<LeaveLedgerEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ledger([FromQuery] Guid? leaveTypeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyLeaveLedgerQuery(leaveTypeId), ct));

    [HttpGet("leave-types")]
    [ProducesResponseType<IReadOnlyList<RequestableLeaveTypeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> LeaveTypes([FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new ListMyRequestableLeaveTypesQuery(), ct)).Localize(localizer));

    [HttpGet("requests")]
    [ProducesResponseType<IReadOnlyList<LeaveRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests([FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new ListMyLeaveRequestsQuery(), ct)).Localize(localizer));

    [HttpGet("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType<LeaveRequestDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestDetail(Guid id, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetMyLeaveRequestQuery(id), ct)).Localize(localizer));

    [HttpGet("requests/preview")]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromQuery] Guid leaveTypeId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromQuery] Guid? editedRequestId, [FromServices] IErrorLocalizer localizer,
        CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new PreviewMyLeaveRequestQuery(leaveTypeId, start, end, editedRequestId), ct)).Localize(localizer));

    [HttpPost("requests")]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] LeaveRequestBody request, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Send(
            new SubmitLeaveRequestCommand(request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.PreviousRequestId), ct))
            .Localize(localizer));

    [HttpPut("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePending(Guid id, [FromBody] LeaveRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdatePendingLeaveRequestCommand(id, request.LeaveTypeId, request.StartDate, request.EndDate, request.Description), ct));

    [HttpPost("requests/" + ApiRoutes.IdParam + "/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawLeaveRequestCommand(id), ct));
}

/// <summary>İzin takvimi: pending/approved leave of the employees the caller may see.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/calendar")]
[Authorize]
public sealed class LeaveCalendarController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LeaveCalendarEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? orgUnitId, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetLeaveCalendarQuery(from, to, orgUnitId), ct)).Localize(localizer));
}

public sealed record OvertimeRequestBody(DateOnly WorkDate, TimeOnly StartTime, int DurationMinutes, string? Description);

public sealed record RecordOvertimeConsentRequest(DateOnly GivenAt, OvertimeConsentSource Source, bool CoversNightWork, Guid? DocumentId);

public sealed record RevokeOvertimeConsentRequest(DateOnly NoticeAt);

public sealed record ConvertOvertimeToTimeOffRequest(decimal? Rate, OvertimeMinutesBasis MinutesBasis, Guid? LeaveTypeId, string? Note);

/// <summary>Self-service overtime ("Talep → Fazla mesai").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/me/overtimes")]
[Authorize]
public sealed class MyOvertimeController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OvertimeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyOvertimeRequestsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<OvertimeSubmissionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] OvertimeRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitMyOvertimeRequestCommand(request.WorkDate, request.StartTime, request.DurationMinutes, request.Description), ct));

    [HttpPost(ApiRoutes.IdParam + "/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawOvertimeRequestCommand(id), ct));
}

/// <summary>Employee card → Fazla mesailer: overtime list, add on behalf, conversions and written consents.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/employees/{employeeId:guid}")]
[Authorize]
public sealed class LeaveEmployeeOvertimeController : ApiControllerBase
{
    [HttpGet("overtimes")]
    [ProducesResponseType<IReadOnlyList<OvertimeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeOvertimeRequestsQuery(employeeId), ct));

    [HttpPost("overtimes")]
    [ProducesResponseType<OvertimeSubmissionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateOnBehalf(Guid employeeId, [FromBody] OvertimeRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateOvertimeRequestOnBehalfCommand(employeeId, request.WorkDate, request.StartTime, request.DurationMinutes, request.Description), ct));

    [HttpPost("overtimes/" + ApiRoutes.IdParam + "/convert-to-time-off")]
    [ProducesResponseType<OvertimeConversionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConvertToTimeOff(Guid employeeId, Guid id, [FromBody] ConvertOvertimeToTimeOffRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ConvertOvertimeToTimeOffCommand(employeeId, id, request.Rate, request.MinutesBasis, request.LeaveTypeId, request.Note), ct));

    [HttpGet("overtime-conversions")]
    [ProducesResponseType<IReadOnlyList<OvertimeConversionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Conversions(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOvertimeConversionsQuery(employeeId), ct));

    [HttpGet("overtime-consents")]
    [ProducesResponseType<IReadOnlyList<OvertimeConsentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Consents(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListOvertimeConsentsQuery(employeeId), ct));

    [HttpPost("overtime-consents")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordConsent(Guid employeeId, [FromBody] RecordOvertimeConsentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecordOvertimeConsentCommand(employeeId, request.GivenAt, request.Source, request.CoversNightWork, request.DocumentId), ct));

    [HttpPost("overtime-consents/" + ApiRoutes.IdParam + "/revoke")]
    [ProducesResponseType<OvertimeConsentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeConsent(Guid employeeId, Guid id, [FromBody] RevokeOvertimeConsentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeOvertimeConsentCommand(employeeId, id, request.NoticeAt), ct));
}
