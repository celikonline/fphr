using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Leave.Api.Controllers;

public sealed record CloneLeaveRuleSetRequest(string Name);

public sealed record SetLeaveTypeDisabledRequest(bool Disabled);

public sealed record LeaveRequestBody(Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string? Description);

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
    public async Task<IActionResult> Requests(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeLeaveRequestsQuery(employeeId), ct));

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

    [HttpGet("requests")]
    [ProducesResponseType<IReadOnlyList<LeaveRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyLeaveRequestsQuery(), ct));

    [HttpPost("requests")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] LeaveRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitLeaveRequestCommand(request.LeaveTypeId, request.StartDate, request.EndDate, request.Description), ct));

    [HttpPut("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePending(Guid id, [FromBody] LeaveRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdatePendingLeaveRequestCommand(id, request.LeaveTypeId, request.StartDate, request.EndDate, request.Description), ct));

    [HttpPost("requests/" + ApiRoutes.IdParam + "/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawLeaveRequestCommand(id), ct));
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
