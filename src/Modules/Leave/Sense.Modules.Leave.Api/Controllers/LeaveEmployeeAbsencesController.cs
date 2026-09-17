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

/// <summary>Employee card → Devamsızlık: İK'nın elle girdiği mazeretsiz/mazeretli/geç kalma kayıtları (append-only).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/employees/{employeeId:guid}/absences")]
[Authorize]
public sealed class LeaveEmployeeAbsencesController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AbsenceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeAbsencesQuery(employeeId, from, to), ct));

    [HttpPost]
    [ProducesResponseType<AbsenceIdResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Record(Guid employeeId, [FromBody] RecordAbsenceRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new RecordAbsenceCommand(employeeId, request.Date, request.Kind, request.Hours, request.Note), ct);
        return result.IsSuccess ? Ok(new AbsenceIdResponse(result.Value)) : Problem(result.Error);
    }

    [HttpPost(ApiRoutes.IdParam + "/reverse")]
    [ProducesResponseType<AbsenceIdResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reverse(Guid employeeId, Guid id, [FromBody] ReverseAbsenceRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(new ReverseAbsenceCommand(employeeId, id, request.Reason), ct);
        return result.IsSuccess ? Ok(new AbsenceIdResponse(result.Value)) : Problem(result.Error);
    }
}
