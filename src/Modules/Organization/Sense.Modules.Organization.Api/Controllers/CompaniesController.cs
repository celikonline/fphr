using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Organization.Application.Companies;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Organization.Api.Controllers;

/// <summary>Request to assign (or, with a null value, clear) a Company's holiday calendar.</summary>
public sealed record AssignHolidayCalendarRequest(Guid? HolidayCalendarId);

/// <summary>
/// Company settings management. Faz 0 only exposes the holiday-calendar assignment added by roadmap
/// item #5 (see docs/analysis/kolayik-video/11.3.02-sirket-yapisi.md: "Şirket eklerken tatil takvimi
/// seçilir ... en alt yapıya kadar bütün yapılara etki eder"); a general Company CRUD surface (legal
/// name, tax fields, etc.) was not part of item #4 and is left for whichever future item adds it.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/organization/companies")]
[Authorize]
public sealed class CompaniesController : ApiControllerBase
{
    /// <summary>Assigns (or clears) the holiday calendar for a Company; applies to the company's whole sub-hierarchy (resolved via IHolidayCalendarLookup).</summary>
    [HttpPut("{id:guid}/holiday-calendar")]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AssignHolidayCalendar(Guid id, [FromBody] AssignHolidayCalendarRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new AssignCompanyHolidayCalendarCommand(id, request.HolidayCalendarId), ct));
}
