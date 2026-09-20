using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.HolidayCalendars;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to create a tenant-custom holiday calendar.</summary>
public sealed record CreateHolidayCalendarRequest(string Name, string CountryCode);

/// <summary>Request to add one entry (a specific holiday) to a calendar.</summary>
public sealed record AddHolidayCalendarEntryRequest(DateOnly Date, string Name, bool IsRecurringAnnually, bool IsHalfDay = false);

/// <summary>Request to rename a tenant-custom holiday calendar.</summary>
public sealed record UpdateHolidayCalendarRequest(string Name);

/// <summary>Request to edit one holiday entry of a tenant-custom calendar.</summary>
public sealed record UpdateHolidayCalendarEntryRequest(DateOnly Date, string Name, bool IsRecurringAnnually, bool IsHalfDay = false);

/// <summary>
/// Holiday calendar management (roadmap Faz 0 item #5). The seeded system "Türkiye Resmi Tatilleri"
/// calendar is created per tenant by Sense.Api.SeedData.DataSeeder, not through this controller;
/// tenants can list it here and add their own custom calendars/entries alongside it. Assigning a
/// calendar to a Company is a separate endpoint - see CompaniesController.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/holiday-calendars")]
[Authorize]
public sealed class HolidayCalendarsController : ApiControllerBase
{
    /// <summary>Lists holiday calendars for the current tenant.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HolidayCalendarDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListHolidayCalendarsQuery(), ct));

    /// <summary>Upcoming holidays (next <paramref name="days"/> days) of the caller's effective calendar, or of the given company's.</summary>
    [HttpGet("upcoming")]
    [ProducesResponseType<IReadOnlyList<UpcomingHolidayDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upcoming([FromQuery] int? days, [FromQuery] Guid? companyId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetUpcomingHolidaysQuery(days ?? GetUpcomingHolidaysQuery.DefaultDays, companyId), ct));

    /// <summary>Lists the entries (holidays) of one calendar, ordered by date.</summary>
    [HttpGet("{id:guid}/entries")]
    [ProducesResponseType<IReadOnlyList<HolidayCalendarEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEntries(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListHolidayCalendarEntriesQuery(id), ct));

    /// <summary>Creates a tenant-custom holiday calendar.</summary>
    [HttpPost]
    [ProducesResponseType<HolidayCalendarDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateHolidayCalendarRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateHolidayCalendarCommand(request.Name, request.CountryCode), ct));

    /// <summary>Adds one entry (a specific holiday) to a calendar.</summary>
    [HttpPost("{id:guid}/entries")]
    [ProducesResponseType<HolidayCalendarEntryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AddEntry(Guid id, [FromBody] AddHolidayCalendarEntryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new AddHolidayCalendarEntryCommand(id, request.Date, request.Name, request.IsRecurringAnnually, request.IsHalfDay), ct));

    /// <summary>Renames a tenant-custom calendar; the system calendar is read-only.</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType<HolidayCalendarDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(Guid id, [FromBody] UpdateHolidayCalendarRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateHolidayCalendarCommand(id, request.Name), ct));

    /// <summary>Deletes a tenant-custom calendar and its entries; the system calendar or one assigned to a company cannot be deleted.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteHolidayCalendarCommand(id), ct));

    /// <summary>Edits one entry of a tenant-custom calendar.</summary>
    [HttpPut("{id:guid}/entries/{entryId:guid}")]
    [ProducesResponseType<HolidayCalendarEntryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntry(Guid id, Guid entryId, [FromBody] UpdateHolidayCalendarEntryRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateHolidayCalendarEntryCommand(id, entryId, request.Date, request.Name, request.IsRecurringAnnually, request.IsHalfDay), ct));

    /// <summary>Deletes one entry of a tenant-custom calendar.</summary>
    [HttpDelete("{id:guid}/entries/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteEntry(Guid id, Guid entryId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteHolidayCalendarEntryCommand(id, entryId), ct));
}
