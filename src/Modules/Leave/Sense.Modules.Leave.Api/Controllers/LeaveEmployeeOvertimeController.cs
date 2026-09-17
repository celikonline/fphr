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

/// <summary>Employee card → Fazla mesailer: overtime list, add on behalf, conversions and written consents.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/employees/{employeeId:guid}")]
[Authorize]
public sealed class LeaveEmployeeOvertimeController : ApiControllerBase
{
    [HttpGet("overtimes")]
    [ProducesResponseType<IReadOnlyList<OvertimeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListEmployeeOvertimeRequestsQuery(employeeId, take), ct));

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
    public async Task<IActionResult> Conversions(Guid employeeId, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListOvertimeConversionsQuery(employeeId, take), ct));

    [HttpGet("overtime-consents")]
    [ProducesResponseType<IReadOnlyList<OvertimeConsentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Consents(Guid employeeId, [FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListOvertimeConsentsQuery(employeeId, take), ct));

    [HttpPost("overtime-consents")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecordConsent(Guid employeeId, [FromBody] RecordOvertimeConsentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecordOvertimeConsentCommand(employeeId, request.GivenAt, request.Source, request.CoversNightWork, request.DocumentId), ct));

    [HttpPost("overtime-consents/" + ApiRoutes.IdParam + "/revoke")]
    [ProducesResponseType<OvertimeConsentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeConsent(Guid employeeId, Guid id, [FromBody] RevokeOvertimeConsentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RevokeOvertimeConsentCommand(employeeId, id, request.NoticeAt), ct));
}
