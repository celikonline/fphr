using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Compensatory;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record RecordCompensationRequest(int Minutes);

/// <summary>Telafi çalışması borcu (11.3.6 [Mevzuat], package A5).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/compensatory")]
[Authorize]
public sealed class CompensatoryController : ApiControllerBase
{
    [HttpGet("employees/{employeeId:guid}")]
    [ProducesResponseType<IReadOnlyList<CompensatoryWorkLedgerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid employeeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCompensatoryLedgerQuery(employeeId, from, to), ct));

    [HttpPost("{ledgerId:guid}/record")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RecordCompensation(Guid ledgerId, [FromBody] RecordCompensationRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RecordCompensationCommand(ledgerId, request.Minutes), ct));
}
