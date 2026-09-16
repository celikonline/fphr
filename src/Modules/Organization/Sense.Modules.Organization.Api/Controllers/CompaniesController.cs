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

/// <summary>Request body for the "Şirket profili" screen (Faz 2, TB-10).</summary>
public sealed record UpdateCompanyProfileRequest(
    string Name,
    string? RegistrationNumber,
    string? TaxNumber,
    string? Address,
    string? City,
    string? Country,
    string? OperatingHoursStart,
    string? OperatingHoursEnd,
    IReadOnlyList<string>? WorkingDays);

/// <summary>Request to set (or, with a null DocumentId, clear) the company profile's logo.</summary>
public sealed record SetCompanyLogoRequest(Guid? LogoDocumentId, string? LogoUrl);

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

    /// <summary>Gets the tenant's primary company profile ("Şirket profili" screen, Faz 2 TB-10).</summary>
    [HttpGet("profile")]
    [ProducesResponseType<CompanyProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCompanyProfileQuery(), ct));

    /// <summary>Updates the tenant's primary company profile: name (renames its OrgUnit), legal/registry fields, and operating hours.</summary>
    [HttpPut("profile")]
    [ProducesResponseType<CompanyProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateCompanyProfileRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new UpdateCompanyProfileCommand(
                request.Name,
                request.RegistrationNumber,
                request.TaxNumber,
                request.Address,
                request.City,
                request.Country,
                request.OperatingHoursStart,
                request.OperatingHoursEnd,
                request.WorkingDays),
            ct));

    /// <summary>Sets (or clears) the company profile's logo reference (documentId from the Documents module upload flow).</summary>
    [HttpPut("profile/logo")]
    [ProducesResponseType<CompanyProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetLogo([FromBody] SetCompanyLogoRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SetCompanyLogoCommand(request.LogoDocumentId, request.LogoUrl), ct));
}
