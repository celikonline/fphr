using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

public sealed record CreateCompanyEventRequest(string Title, string? Description, DateOnly EventDate, CompanyEventType Type, Guid? VisibilityOrgUnitId);

public sealed record UpdateCompanyEventRequest(string Title, string? Description, DateOnly EventDate, CompanyEventType Type, Guid? VisibilityOrgUnitId);

/// <summary>Şirket etkinlik takvimi (senseik-027, roadmap #71/P-19): manuel etkinlikler + doğum günü/izin başlangıcı türetmeleri.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/announcements/events")]
[Authorize]
public sealed class CompanyEventsController : ApiControllerBase
{
    [HttpGet("calendar")]
    [ProducesResponseType<IReadOnlyList<CompanyCalendarEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Calendar([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? orgUnitId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCompanyCalendarQuery(from, to, orgUnitId), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyEventRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new CreateCompanyEventCommand(request.Title, request.Description, request.EventDate, request.Type, request.VisibilityOrgUnitId), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyEventRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateCompanyEventCommand(id, request.Title, request.Description, request.EventDate, request.Type, request.VisibilityOrgUnitId), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteCompanyEventCommand(id), ct));
}
