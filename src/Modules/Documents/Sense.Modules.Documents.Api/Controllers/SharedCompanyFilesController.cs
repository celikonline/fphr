using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

/// <summary>Request to initiate a shared company file upload.</summary>
public sealed record InitiateSharedFileUploadRequest(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    SharedFileVisibility Visibility = SharedFileVisibility.Everyone,
    IReadOnlyList<string>? AllowedRoles = null,
    Guid? TypeId = null,
    bool? VisibleOnMobile = null);

/// <summary>Request to confirm a shared company file upload with ETag.</summary>
public sealed record ConfirmSharedFileUploadRequest(string ETag, long ActualFileSizeBytes);

/// <summary>
/// Shared company files (senseik-028): tenant-wide (or role-restricted) file library distinct from the
/// employee-scoped documents under <see cref="DocumentsController"/>. Reuses the same presigned-URL upload flow.
/// </summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/documents/shared-files")]
[Authorize]
public sealed class SharedCompanyFilesController : ApiControllerBase
{
    /// <summary>Lists shared company files visible to the caller (paginated).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<SharedFileListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] PagedQuery paging, [FromQuery] bool? mobileOnly, [FromQuery] Guid? typeId, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListSharedFilesQuery(paging, mobileOnly, typeId), ct));

    /// <summary>Gets a single shared company file with a presigned download URL.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<SharedFileDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetSharedFileQuery(id), ct));

    /// <summary>Tenant's shared-file storage quota usage.</summary>
    [HttpGet("quota")]
    [ProducesResponseType<SharedFileQuotaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Quota(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetSharedFileQuotaQuery(), ct));

    /// <summary>Initiates a shared company file upload and returns a presigned URL.</summary>
    [HttpPost("initiate-upload")]
    [ProducesResponseType<InitiateSharedFileUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitiateUpload([FromBody] InitiateSharedFileUploadRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new InitiateSharedFileUploadCommand(request.FileName, request.ContentType, request.FileSizeBytes, request.Visibility, request.AllowedRoles, request.TypeId, request.VisibleOnMobile),
            ct);
        return FromResult(result);
    }

    /// <summary>Confirms a shared company file upload with ETag validation.</summary>
    [HttpPost(ApiRoutes.IdParam + "/confirm-upload")]
    [ProducesResponseType<ConfirmSharedFileUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmUpload(Guid id, [FromBody] ConfirmSharedFileUploadRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new ConfirmSharedFileUploadCommand(id, request.ETag, request.ActualFileSizeBytes),
            ct);
        return FromResult(result);
    }

    /// <summary>Deletes a shared company file and removes it from storage.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteSharedFileCommand(id), ct));

    /// <summary>Sets a file's type and "mobilde görünsün" flag (E8).</summary>
    [HttpPut(ApiRoutes.IdParam + "/classification")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Classify(Guid id, [FromBody] ClassifySharedFileRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ClassifySharedFileCommand(id, request.TypeId, request.VisibleOnMobile), ct));

    /// <summary>Downloads the selected (or filtered) visible shared files as one zip (E8).</summary>
    [HttpPost("download-zip")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DownloadZip([FromBody] DownloadSharedFilesZipRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Query(new DownloadSharedFilesZipQuery(request.FileIds, request.MobileOnly, request.TypeId), ct);
        return result.IsSuccess ? File(result.Value.Content, "application/zip", result.Value.FileName) : Problem(result.Error);
    }
}

/// <summary>Request to (re)classify a shared file.</summary>
public sealed record ClassifySharedFileRequest(Guid? TypeId, bool VisibleOnMobile);

/// <summary>Zip download selection: explicit ids and/or list filters.</summary>
public sealed record DownloadSharedFilesZipRequest(IReadOnlyList<Guid>? FileIds, bool? MobileOnly, Guid? TypeId);
