using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Employee.Application.CardPdf;
using Sense.Modules.Employee.Application.Employees;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Employee.Api.Controllers;

/// <summary>Documents the employer issues about an employee (today: the çalışma belgesi of a terminated employee).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/employees")]
[Authorize]
public sealed class EmployeeDocumentsController : ApiControllerBase
{
    /// <summary>Çalışma belgesi (4857 md.28) as PDF; 404 while the employee is not terminated.</summary>
    [HttpGet(ApiRoutes.IdParam + "/work-certificate")]
    public async Task<IActionResult> WorkCertificate(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetWorkCertificateQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }

    /// <summary>Employee card as PDF: comma-separated tab keys, optional period (yyyy-MM-dd) for event lists.</summary>
    [HttpGet(ApiRoutes.IdParam + "/card-pdf")]
    public async Task<IActionResult> CardPdf(
        Guid id, [FromQuery] string? sections, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        ToFile(await Dispatcher.Query(new GetEmployeeCardPdfQuery(id, SplitSections(sections), from, to), ct));

    /// <summary>The caller's own card as PDF (self-service).</summary>
    [HttpGet("me/card-pdf")]
    public async Task<IActionResult> MyCardPdf(
        [FromQuery] string? sections, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        ToFile(await Dispatcher.Query(new GetMyEmployeeCardPdfQuery(SplitSections(sections), from, to), ct));

    private static IReadOnlyList<string> SplitSections(string? sections) =>
        (sections ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private IActionResult ToFile(Result<ExportFileResult> result) =>
        result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
}
