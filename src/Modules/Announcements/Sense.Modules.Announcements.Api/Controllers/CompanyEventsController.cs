using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.CompanyEvents;
using Sense.Modules.Announcements.Domain;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

public sealed record CreateCompanyEventRequest(string Title, string? Description, DateOnly EventDate, CompanyEventType Type, Guid? VisibilityOrgUnitId,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    bool? IsAllDay = null,
    CompanyEventMode? Mode = null,
    string? Location = null,
    string? OnlineUrl = null,
    bool NotifyInvitees = false,
    Guid? CategoryId = null,
    IReadOnlyList<Guid>? InviteeEmployeeIds = null);

public sealed record UpdateCompanyEventRequest(string Title, string? Description, DateOnly EventDate, CompanyEventType Type, Guid? VisibilityOrgUnitId,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    bool? IsAllDay = null,
    CompanyEventMode? Mode = null,
    string? Location = null,
    string? OnlineUrl = null,
    bool NotifyInvitees = false,
    Guid? CategoryId = null,
    IReadOnlyList<Guid>? InviteeEmployeeIds = null);

public sealed record CompanyEventCategoryRequest(string Name, string Color);

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
            new CreateCompanyEventCommand(request.Title, request.Description, request.EventDate, request.Type, request.VisibilityOrgUnitId,
                request.StartsAt, request.EndsAt, request.IsAllDay, request.Mode, request.Location, request.OnlineUrl,
                request.NotifyInvitees, request.CategoryId, request.InviteeEmployeeIds), ct));

    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCompanyEventRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateCompanyEventCommand(id, request.Title, request.Description, request.EventDate, request.Type, request.VisibilityOrgUnitId,
                request.StartsAt, request.EndsAt, request.IsAllDay, request.Mode, request.Location, request.OnlineUrl,
                request.NotifyInvitees, request.CategoryId, request.InviteeEmployeeIds), ct));

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<CompanyEventCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Categories(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCompanyEventCategoriesQuery(), ct));

    [HttpPost("categories")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCategory([FromBody] CompanyEventCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateCompanyEventCategoryCommand(request.Name, request.Color), ct));

    [HttpPut("categories/{categoryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateCategory(Guid categoryId, [FromBody] CompanyEventCategoryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateCompanyEventCategoryCommand(categoryId, request.Name, request.Color), ct));

    [HttpDelete("categories/{categoryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteCategory(Guid categoryId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteCompanyEventCategoryCommand(categoryId), ct));

    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteCompanyEventCommand(id), ct));
}
