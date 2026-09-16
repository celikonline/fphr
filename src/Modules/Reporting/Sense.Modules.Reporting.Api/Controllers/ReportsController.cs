using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Reporting.Application;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Reporting.Api.Controllers;

public sealed record RequestReportRequest(string ReportKey, Dictionary<string, string>? Parameters);

/// <summary>Report catalog + on-demand generation + execution history (roadmap #70, R0: synchronous generation, email-linked delivery).</summary>
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

    /// <summary>Requests a report; the response's Status reflects the (synchronous, R0) outcome.</summary>
    [HttpPost("executions")]
    [ProducesResponseType<ReportExecutionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RequestReport([FromBody] RequestReportRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RequestReportCommand(request.ReportKey, request.Parameters), ct));

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

    /// <summary>Downloads a succeeded execution's generated file.</summary>
    [HttpGet("executions/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new GetReportExecutionFileQuery(id), ct);
        return result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
    }
}
