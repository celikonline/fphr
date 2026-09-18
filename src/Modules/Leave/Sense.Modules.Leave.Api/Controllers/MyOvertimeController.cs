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

/// <summary>Self-service overtime ("Talep → Fazla mesai").</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/leave/me/overtimes")]
[Authorize]
public sealed class MyOvertimeController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OvertimeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int take = PagingDefaults.MaxPageSize, CancellationToken ct = default) =>
        FromResult(await Dispatcher.Query(new ListMyOvertimeRequestsQuery(take), ct));

    [HttpPost]
    [ProducesResponseType<OvertimeSubmissionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] OvertimeRequestBody request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitMyOvertimeRequestCommand(request.WorkDate, request.StartTime, request.DurationMinutes, request.Description), ct));

    [HttpPost(ApiRoutes.IdParam + "/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new WithdrawOvertimeRequestCommand(id), ct));
}
