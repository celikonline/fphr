using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Application.ImportJobs;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Kernel.Results;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.DataTransfer.Api.Controllers;

public sealed record SaveImportJobMappingRequest(IReadOnlyList<ColumnMappingDto> Mapping, string Mode, string FailurePolicy);

/// <summary>Veri aktarım merkezi (Faz 0 items 7/8): templates, template download, import jobs (upload → mapping → run → result).</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/datatransfer")]
[Authorize]
public sealed class DataTransferController : ApiControllerBase
{
    // Multipart envelope on top of the 10 MB file limit.
    private const long MaxRequestBytes = ImportLimits.MaxFileSizeBytes + (1024 * 1024);

    /// <summary>Import templates with localized columns (?lang=tr|en, default request culture).</summary>
    [HttpGet("templates")]
    [ProducesResponseType<IReadOnlyList<ImportTemplateDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Templates([FromQuery] string? lang, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListImportTemplatesQuery(lang), ct));

    /// <summary>Template workbook (xlsx).</summary>
    [HttpGet("templates/{code}/download")]
    public async Task<IActionResult> DownloadTemplate(string code, [FromQuery] string? lang, CancellationToken ct) =>
        FromFile(await Dispatcher.Query(new DownloadImportTemplateQuery(code, lang), ct));

    /// <summary>Uploads an .xlsx/.csv file (≤ 10 MB, ≤ 20,000 rows) for a template; returns the analyzed job.</summary>
    [HttpPost("import-jobs")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<ImportJobDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload([FromForm] string templateCode, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(Error.Validation(DataTransferErrors.FileRequired));
        }

        if (file.Length > ImportLimits.MaxFileSizeBytes)
        {
            return Problem(Error.Rule(DataTransferErrors.FileTooLarge, ("max", "10 MB")));
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);
        return FromResult(await Dispatcher.Send(new CreateImportJobCommand(templateCode ?? string.Empty, file.FileName, buffer.ToArray()), ct));
    }

    /// <summary>Import history.</summary>
    [HttpGet("import-jobs")]
    [ProducesResponseType<PagedResult<ImportJobListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListImportJobsQuery(paging), ct));

    [HttpGet("import-jobs/" + ApiRoutes.IdParam)]
    [ProducesResponseType<ImportJobDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetImportJobQuery(id), ct));

    /// <summary>Saves column mapping and options; 422 IMPORT_UNMAPPED_REQUIRED (args.missing = comma-separated keys) when required columns are unmapped.</summary>
    [HttpPut("import-jobs/" + ApiRoutes.IdParam + "/mapping")]
    [ProducesResponseType<ImportJobDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveMapping(Guid id, [FromBody] SaveImportJobMappingRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new SaveImportJobMappingCommand(id, request.Mapping ?? [], request.Mode, request.FailurePolicy), ct));

    /// <summary>Runs the mapped job synchronously.</summary>
    [HttpPost("import-jobs/" + ApiRoutes.IdParam + "/run")]
    [ProducesResponseType<ImportJobDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new RunImportJobCommand(id), ct));

    /// <summary>Row outcomes (?outcome&amp;page&amp;pageSize).</summary>
    [HttpGet("import-jobs/" + ApiRoutes.IdParam + "/rows")]
    [ProducesResponseType<PagedResult<ImportJobRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rows(Guid id, [FromQuery] PagedQuery paging, [FromQuery] string? outcome, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListImportJobRowsQuery(id, outcome, paging), ct));

    /// <summary>Result workbook: source columns + "Sonuç" + "Hata açıklaması".</summary>
    [HttpGet("import-jobs/" + ApiRoutes.IdParam + "/result")]
    public async Task<IActionResult> Result(Guid id, [FromQuery] string? lang, CancellationToken ct) =>
        FromFile(await Dispatcher.Query(new DownloadImportResultQuery(id, lang), ct));

    private IActionResult FromFile(Result<FileDownload> result) =>
        result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error);
}
