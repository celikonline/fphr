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
    IReadOnlyList<string>? AllowedRoles = null);

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
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListSharedFilesQuery(paging), ct));

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
            new InitiateSharedFileUploadCommand(request.FileName, request.ContentType, request.FileSizeBytes, request.Visibility, request.AllowedRoles),
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
}
