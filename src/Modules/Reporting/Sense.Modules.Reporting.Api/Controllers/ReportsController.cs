using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Reporting.Application;
using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Reporting.Api.Controllers;

public sealed record RequestReportRequest(string ReportKey, Dictionary<string, string>? Parameters, bool? Notify = null);

/// <summary>Report catalog, one-time custom reports, generation, and execution history.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/reports")]
[Authorize]
public sealed class ReportsController : ApiControllerBase
{
    /// <summary>Reports the caller may generate.</summary>
    [HttpGet("catalog")]
    [ProducesResponseType<IReadOnlyList<ReportCatalogItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListReportCatalogQuery(), ct));

    /// <summary>Fields the caller may use to build a one-time report.</summary>
    [HttpGet("custom/catalog")]
    [ProducesResponseType<CustomReportCatalog>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CustomCatalog(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetCustomReportCatalogQuery(), ct));

    /// <summary>Requests a report; async providers return queued executions.</summary>
    [HttpPost("executions")]
    [ProducesResponseType<ReportExecutionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestReport([FromBody] RequestReportRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RequestReportCommand(request.ReportKey, request.Parameters, request.Notify ?? true), ct));

    /// <summary>The caller's own report execution history, newest first.</summary>
    [HttpGet("executions")]
    [ProducesResponseType<PagedResult<ReportExecutionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMine([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyReportExecutionsQuery(paging), ct));

    /// <summary>One of the caller's own report executions.</summary>
    [HttpGet("executions/{id:guid}")]
    [ProducesResponseType<ReportExecutionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetReportExecutionQuery(id), ct));

    /// <summary>A succeeded execution's workbook as grid data: sheets, columns and one searched/sorted/paged slice of rows.</summary>
    [HttpGet("executions/{id:guid}/data")]
    [ProducesResponseType<ReportDataDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Data(Guid id, [FromQuery] int sheet, [FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetReportExecutionDataQuery(id, sheet, paging), ct));

    /// <summary>Downloads a succeeded execution's generated file (or renders the saved workbook snapshot as PDF).</summary>
    [HttpGet("executions/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, [FromQuery] string? format, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetReportExecutionFileQuery(id, format), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }
}
