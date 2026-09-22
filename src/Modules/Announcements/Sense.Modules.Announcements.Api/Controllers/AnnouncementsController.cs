using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sense.Modules.Announcements.Application.Announcements;
using Sense.Modules.Announcements.Application.Attachments;
using Sense.Modules.Announcements.Application.ReadRecords;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Domain.Attachments;
using Sense.Shared.Web.Controllers;

namespace Sense.Modules.Announcements.Api.Controllers;

public sealed record CreateAnnouncementRequest(string Title, string Content, AnnouncementType Type, AnnouncementPriority Priority, string? TargetAudience);

public sealed record UpdateAnnouncementRequest(string Title, string Content, AnnouncementType Type, AnnouncementPriority Priority, string? TargetAudience);

public sealed record PublishAnnouncementRequest(DateTime? ExpiresAt);

public sealed record InitiateAnnouncementAttachmentUploadRequest(string FileName, string ContentType, long FileSizeBytes, AnnouncementAttachmentKind Kind);

public sealed record ConfirmAnnouncementAttachmentUploadRequest(string ETag, long ActualFileSizeBytes);

/// <summary>Kurumsal duyurular (roadmap: Announcements modülü). Yayınlanmış duyurular Faz 1 ana sayfa akışında listelenir.</summary>
[ApiVersion(ApiRoutes.DefaultVersion)]
[Route("api/v{version:apiVersion}/announcements")]
[Authorize]
public sealed class AnnouncementsController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AnnouncementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] bool? publishedOnly, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAnnouncementsQuery(publishedOnly), ct));

    /// <summary>Çalışanın duyuru akışı: yayındaki (süresi geçmemiş) duyurular + okundu işareti.</summary>
    [HttpGet("feed")]
    [ProducesResponseType<IReadOnlyList<AnnouncementFeedItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Feed(CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyAnnouncementFeedQuery(), ct));

    [HttpPost(ApiRoutes.IdParam + "/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new MarkAnnouncementReadCommand(id), ct));

    [HttpGet(ApiRoutes.IdParam + "/read-count")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReadCount(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new CountAnnouncementReadersQuery(id), ct));

    [HttpGet(ApiRoutes.IdParam)]
    [ProducesResponseType<AnnouncementDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new GetAnnouncementQuery(id), ct));

    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateAnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new CreateAnnouncementCommand(request.Title, request.Content, request.Type, request.Priority, request.TargetAudience), ct));

    /// <summary>Taslak duyuruyu düzenle (tam sayfa editör).</summary>
    [HttpPut(ApiRoutes.IdParam)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new UpdateAnnouncementCommand(id, request.Title, request.Content, request.Type, request.Priority, request.TargetAudience), ct));

    [HttpPost(ApiRoutes.IdParam + "/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, [FromBody] PublishAnnouncementRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new PublishAnnouncementCommand(id, request.ExpiresAt), ct));

    [HttpPost(ApiRoutes.IdParam + "/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ArchiveAnnouncementCommand(id), ct));

    /// <summary>Duyuru ekleri (yönetim/editör): gömülü resimler ve indirilebilir dosyalar, taze indirme URL'siyle.</summary>
    [HttpGet(ApiRoutes.IdParam + "/attachments")]
    [ProducesResponseType<IReadOnlyList<AnnouncementAttachmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Attachments(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListAnnouncementAttachmentsQuery(id), ct));

    /// <summary>Duyuru ekleri (çalışan akışı): yalnızca yayında ve süresi geçmemiş duyurular için.</summary>
    [HttpGet("feed/" + ApiRoutes.IdParam + "/attachments")]
    [ProducesResponseType<IReadOnlyList<AnnouncementAttachmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyAttachments(Guid id, CancellationToken ct) =>
        FromResult(await Dispatcher.Query(new ListMyAnnouncementAttachmentsQuery(id), ct));

    /// <summary>Bir eki yüklemeye başlar; imzalı URL döner (yalnız taslak duyuru).</summary>
    [HttpPost(ApiRoutes.IdParam + "/attachments/initiate-upload")]
    [ProducesResponseType<InitiateAnnouncementAttachmentUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> InitiateAttachmentUpload(Guid id, [FromBody] InitiateAnnouncementAttachmentUploadRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(
            new InitiateAnnouncementAttachmentUploadCommand(id, request.FileName, request.ContentType, request.FileSizeBytes, request.Kind), ct));

    /// <summary>Yüklemeyi ETag ile onaylar; anında kullanılabilir bir indirme URL'si döner.</summary>
    [HttpPost(ApiRoutes.IdParam + "/attachments/{attachmentId:guid}/confirm-upload")]
    [ProducesResponseType<ConfirmAnnouncementAttachmentUploadResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmAttachmentUpload(Guid id, Guid attachmentId, [FromBody] ConfirmAnnouncementAttachmentUploadRequest request, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new ConfirmAnnouncementAttachmentUploadCommand(attachmentId, request.ETag, request.ActualFileSizeBytes), ct));

    /// <summary>Eki siler (yalnız taslak duyuru).</summary>
    [HttpDelete(ApiRoutes.IdParam + "/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken ct) =>
        FromResult(await Dispatcher.Send(new DeleteAnnouncementAttachmentCommand(attachmentId), ct));
}
