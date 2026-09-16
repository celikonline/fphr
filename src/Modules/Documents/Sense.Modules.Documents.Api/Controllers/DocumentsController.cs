using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Documents.Application.Documents;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Documents.Api.Controllers;

/// <summary>Request to initiate a file upload.</summary>
public sealed record InitiateUploadRequest(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? BucketName = "documents");

/// <summary>Request to confirm a file upload with ETag. <paramref name="ExpiryDate"/> is optional - the document's
/// own validity/expiry date (e.g. driver's license, residence permit, health report), not the upload deadline.</summary>
public sealed record ConfirmUploadRequest(
    string ETag,
    long ActualFileSizeBytes,
    DateOnly? ExpiryDate = null);

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
            new ConfirmUploadCommand(id, request.ETag, request.ActualFileSizeBytes, request.ExpiryDate),
            ct);
        return FromResult(result);
    }

    /// <summary>Self-service: initiates a file upload for the caller's own use (e.g. to satisfy their own document requirement).</summary>
    [HttpPost("me/initiate-upload")]
    [ProducesResponseType<InitiateUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitiateMyUpload([FromBody] InitiateUploadRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new InitiateMyUploadCommand(request.FileName, request.ContentType, request.FileSizeBytes, request.BucketName),
            ct);
        return FromResult(result);
    }

    /// <summary>Self-service: confirms an upload initiated via <c>me/initiate-upload</c>.</summary>
    [HttpPost("me/" + ApiRoutes.IdParam + "/confirm-upload")]
    [ProducesResponseType<ConfirmUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmMyUpload(Guid id, [FromBody] ConfirmUploadRequest request, CancellationToken ct)
    {
        var result = await Dispatcher.Send(
            new ConfirmMyUploadCommand(id, request.ETag, request.ActualFileSizeBytes, request.ExpiryDate),
            ct);
        return FromResult(result);
    }

    /// <summary>Deletes a document and removes file from storage.</summary>
    [HttpDelete(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteDocumentCommand(id), ct));

    /// <summary>Paged report of documents that are expiring soon or already expired (S-6).</summary>
    [HttpGet("expiring")]
    [ProducesResponseType<PagedResult<ExpiringDocumentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Expiring(
        [FromQuery] int withinDays,
        [FromQuery] DocumentExpiryState? state,
        [FromQuery] PagedQuery paging,
        CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetExpiringDocumentsQuery(withinDays, state, paging), ct));
}
