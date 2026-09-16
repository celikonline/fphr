using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Attendance.Application.Availability;
using Sense.Modules.Attendance.Domain.Availability;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Attendance.Api.Controllers;

public sealed record SubmitMyAvailabilityRequest(DateOnly Date, TimeOnly? Start, TimeOnly? End, AvailabilityKind Kind, string? Note);

/// <summary>"Müsait değilim" (11.3.6, package A5).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/attendance/availability")]
[Authorize]
public sealed class AvailabilityController : ApiControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<EmployeeAvailabilityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyAvailabilityQuery(from, to), ct));

    [HttpPost("me")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitMyAvailabilityRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SubmitMyAvailabilityCommand(request.Date, request.Start, request.End, request.Kind, request.Note), ct));

    [HttpPost("me/{availabilityId:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid availabilityId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CancelMyAvailabilityCommand(availabilityId), ct));

    /// <summary>Planlayıcı görünümü: bir çalışanın müsaitlik girişleri.</summary>
    [HttpGet("employees/{employeeId:guid}")]
    [ProducesResponseType<IReadOnlyList<EmployeeAvailabilityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ForEmployee(Guid employeeId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListEmployeeAvailabilityQuery(employeeId, from, to), ct));
}
