using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.Documents;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

/// <summary>Request to initiate a file upload.</summary>
public sealed record InitiateUploadRequest(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? BucketName = "documents");

/// <summary>Request to confirm a file upload with ETag.</summary>
public sealed record ConfirmUploadRequest(
    string ETag,
    long ActualFileSizeBytes);

/// <summary>File document management: upload, download, delete with presigned URLs.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/documents")]
[Authorize]
public sealed class DocumentsController : ApiControllerBase
{
    /// <summary>Lists all documents for the current tenant (paginated).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DocumentListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery paging, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListDocumentsQuery(paging), ct));

    /// <summary>Gets a single document with presigned download URL.</summary>
    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<DocumentDetailDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetDocumentQuery(id), ct));

    /// <summary>Initiates a file upload and returns a presigned URL.</summary>
    [HttpPost("initiate-upload")]
    [ProducesResponseType<InitiateUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitiateUpload([FromBody] InitiateUploadRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new InitiateUploadCommand(request.FileName, request.ContentType, request.FileSizeBytes, request.BucketName),
            ct);
        return FromResult(result);
    }

    /// <summary>Confirms a file upload with ETag validation.</summary>
    [HttpPost(ApiRoutes.IdParam + "/confirm-upload")]
    [ProducesResponseType<ConfirmUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmUpload(Guid id, [FromBody] ConfirmUploadRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new ConfirmUploadCommand(id, request.ETag, request.ActualFileSizeBytes),
            ct);
        return FromResult(result);
    }

    /// <summary>Deletes a document and removes file from storage.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteDocumentCommand(id), ct));
}
