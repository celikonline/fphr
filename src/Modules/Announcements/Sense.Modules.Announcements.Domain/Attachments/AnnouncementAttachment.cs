using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Announcements.Domain.Attachments;

/// <summary>
/// <see cref="Inline"/> is a picture embedded in the rich-text content (referenced from HTML by
/// <c>data-attachment-id</c>, never by a permanent URL - see <see cref="AnnouncementsLimits.ContentMaxLength"/>'s
/// doc); <see cref="File"/> is a general downloadable attachment listed alongside the announcement (PDF, Office
/// document, ...). Both share the same presigned-upload flow; the kind only changes how the web app renders it.
/// </summary>
public enum AnnouncementAttachmentKind
{
    Inline = 0,
    File = 1,
}

/// <summary>
/// One file uploaded to a duyuru (draft or published): an inline editor image or a downloadable attachment. Storage
/// itself is presigned-URL based (<see cref="Sense.Shared.Contracts.Storage.IFileStorage"/>, same two-step
/// initiate/confirm flow as Documents' shared company files) - this entity only tracks the metadata and confirms the
/// upload actually landed (ETag/size), never the file bytes.
/// </summary>
public sealed class AnnouncementAttachment : TenantEntity<Guid>
{
    // ReSharper disable once UnusedMember.Local
    private AnnouncementAttachment() { }

    /// <summary>Logical bucket name passed to <see cref="Sense.Shared.Contracts.Storage.IFileStorage"/>; namespaced per tenant by the storage adapter itself.</summary>
    public const string BucketName = "announcement-attachments";

    public Guid AnnouncementId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public AnnouncementAttachmentKind Kind { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public DateTime UploadedAt { get; private set; }

    /// <summary>False until <see cref="Confirm"/> runs (the client finished the presigned PUT and reported an ETag);
    /// an attachment that never confirms is orphaned storage, cleaned up like Documents' pending uploads.</summary>
    public bool IsConfirmed { get; private set; }

    public static AnnouncementAttachment CreatePending(
        Guid tenantId, Guid announcementId, string fileName, string storageKey, string contentType, AnnouncementAttachmentKind kind, Guid uploadedByUserId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AnnouncementId = announcementId,
            FileName = fileName,
            StorageKey = storageKey,
            ContentType = contentType,
            Kind = kind,
            UploadedByUserId = uploadedByUserId,
            UploadedAt = DateTime.UtcNow,
            IsConfirmed = false,
        };

    /// <summary><paramref name="actualSizeBytes"/> comes from storage (IFileStorage.ConfirmUploadAsync's return), never the client's declared size.</summary>
    public void Confirm(long actualSizeBytes)
    {
        SizeBytes = actualSizeBytes;
        IsConfirmed = true;
    }
}
