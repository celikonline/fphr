using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.OccupationalSafety.Application.Incidents;
using Sense.Modules.OccupationalSafety.Domain.Incidents;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.OccupationalSafety.Api.Controllers;

public sealed record RecordIncidentRequest(OhsIncidentKind Kind, DateOnly EventDate, Guid? LocationId, Guid? EmployeeId, string Description);

public sealed record ReportNearMissRequest(DateOnly EventDate, Guid? LocationId, string Description);

public sealed record MarkSgkNotifiedRequest(DateOnly NotifiedOn);

public sealed record CloseIncidentRequest(string CorrectiveAction);

/// <summary>İş kazası / ramak kala kayıtları (6331 md.14); iş kazasında SGK bildirim son günü sunucuda hesaplanır.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/ohs/incidents")]
[Authorize]
public sealed class IncidentsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OhsIncidentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListIncidentsQuery(), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Record([FromBody] RecordIncidentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new RecordIncidentCommand(request.Kind, request.EventDate, request.LocationId, request.EmployeeId, request.Description), ct));

    [HttpPost(ApiRoutes.IdParam + "/sgk-notified")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkSgkNotified(Guid id, [FromBody] MarkSgkNotifiedRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkIncidentSgkNotifiedCommand(id, request.NotifiedOn), ct));

    [HttpPost(ApiRoutes.IdParam + "/close")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Close(Guid id, [FromBody] CloseIncidentRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CloseIncidentCommand(id, request.CorrectiveAction), ct));

    /// <summary>Self-service: the caller's own near-miss reports.</summary>
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<OhsIncidentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMy(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyIncidentReportsQuery(), ct));

    /// <summary>Self-service near-miss report.</summary>
    [HttpPost("my")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReportNearMiss([FromBody] ReportNearMissRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ReportNearMissCommand(request.EventDate, request.LocationId, request.Description), ct));
}
