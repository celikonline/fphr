using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.Companies;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to assign (or, with a null value, clear) a Company's holiday calendar.</summary>
public sealed record AssignHolidayCalendarRequest(Guid? HolidayCalendarId);

/// <summary>Request to create a company (root Company-level unit + holiday calendar).</summary>
public sealed record CreateCompanyRequest(string Name, Guid? HolidayCalendarId);

/// <summary>Companies: list, create ("Şirket ekle": Ad + Tatil takvimi), and holiday-calendar assignment.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/companies")]
[Authorize]
public sealed class CompaniesController : ApiControllerBase
{
    /// <summary>Lists the tenant's companies (active Company-level units).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CompanyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListCompaniesQuery(), ct));

    /// <summary>Creates a company: a parentless Company-level org unit and its Company row, atomically.</summary>
    [HttpPost]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateCompanyCommand(request.Name, request.HolidayCalendarId), ct));

    /// <summary>Assigns (or clears) the holiday calendar for a Company; applies to the company's whole sub-hierarchy (resolved via IHolidayCalendarLookup).</summary>
    [HttpPut("{id:guid}/holiday-calendar")]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignHolidayCalendar(Guid id, [FromBody] AssignHolidayCalendarRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AssignCompanyHolidayCalendarCommand(id, request.HolidayCalendarId), ct));
}
