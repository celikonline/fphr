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

    /// <summary>senseik-036 md.7: hastalık izni 6 hafta devamsızlık eşiği sayacı (4857 md.25/I-b, bilgi amaçlı).</summary>
    [HttpGet("sick-leave-absence-status")]
    [ProducesResponseType<SickLeaveAbsenceStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SickLeaveAbsenceStatus(Guid employeeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetEmployeeSickLeaveAbsenceStatusQuery(employeeId), ct));

    [HttpGet("requests")]
    [ProducesResponseType<IReadOnlyList<LeaveRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests(Guid employeeId, [FromServices] IErrorLocalizer localizer, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult((await Dispatcher.Query(new ListEmployeeLeaveRequestsQuery(employeeId, take), ct)).Localize(localizer));

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
            new CreateLeaveRequestOnBehalfCommand(
                employeeId, request.LeaveTypeId, request.StartDate, request.EndDate, request.Description, request.BypassApproval,
                request.StartTime, request.EndTime, request.HalfDayPeriod), ct))
            .Localize(localizer));

    [HttpGet("requests/preview")]
    [ProducesResponseType<LeaveRequestPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(
        Guid employeeId, [FromQuery] Guid leaveTypeId, [FromQuery] DateOnly start, [FromQuery] DateOnly end, [FromQuery] TimeOnly? startTime,
        [FromQuery] TimeOnly? endTime, [FromQuery] HalfDayPeriod? halfDayPeriod, [FromServices] IErrorLocalizer localizer, CancellationToken ct) =>
        FromResult((await Dispatcher.Query(new PreviewEmployeeLeaveRequestQuery(employeeId, leaveTypeId, start, end, startTime, endTime, halfDayPeriod), ct))
            .Localize(localizer));

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
