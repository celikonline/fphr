using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Leave.Application.Absences;
using Sense.Modules.Leave.Application.Balances;
using Sense.Modules.Leave.Application.Calendar;
using Sense.Modules.Leave.Application.Overtime;
using Sense.Modules.Leave.Application.Requests;
using Sense.Modules.Leave.Application.RuleSets;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Absences;
using Sense.Modules.Leave.Domain.Overtime;
using Sense.Modules.Leave.Domain.RuleSets;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;
using Sense.Shared.Web.Localization;

namespace Sense.Modules.Leave.Api.Controllers;

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
    public async Task<IActionResult> Requests([FromServices] IErrorLocalizer localizer, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult((await Dispatcher.Query(new ListMyLeaveRequestsQuery(take), ct)).Localize(localizer));

    [HttpGet("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType<LeaveRequestDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestDetail(Guid id, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new GetMyLeaveRequestQuery(id), ct)).Localize(localizer));

    [HttpGet("requests/preview")]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        [FromQuery] Guid leaveTypeId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromQuery] Guid? editedRequestId,
        [FromQuery] TimeOnly? startTime, [FromQuery] TimeOnly? endTime, [FromQuery] HalfDayPeriod? halfDayPeriod, [FromServices] IErrorLocalizer localizer,
        CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new PreviewMyLeaveRequestQuery(leaveTypeId, start, end, editedRequestId, startTime, endTime, halfDayPeriod), ct))
            .Localize(localizer));

    [HttpPost("requests")]
    [ProducesResponseType<LeaveRequestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] LeaveRequestBody request, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Send(
            new SubmitLeaveRequestCommand(
                request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.PreviousRequestId,
                request.StartTime, request.EndTime, request.HalfDayPeriod), ct))
            .Localize(localizer));

    [HttpPut("requests/" + ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePending(Guid id, [FromBody] LeaveRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdatePendingLeaveRequestCommand(
                id, request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.StartTime, request.EndTime, request.HalfDayPeriod),
            ct));

    [HttpPost("requests/" + ApiRoutes.IdParam + "/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawLeaveRequestCommand(id), ct));
}
