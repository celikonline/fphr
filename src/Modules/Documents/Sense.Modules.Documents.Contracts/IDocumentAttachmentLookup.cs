namespace Sense.Modules.Documents.Contracts;

/// <summary>Başka modül kaydına (ör. izin talebi) eklenen belgenin özeti: dosya adı talep detayında gösterilir.
/// <paramref name="UploadedBy"/>/<paramref name="EmployeeId"/> are the ownership signals the caller checks before
/// letting a document be attached to someone else's record (IDOR guard) - see <see cref="IDocumentAttachmentLookup"/>.</summary>
public sealed record AttachedDocumentInfo(Guid DocumentId, string FileName, string ContentType, long Size, Guid UploadedBy, Guid? EmployeeId);

/// <summary>A short-lived presigned URL to download a previously attached document, plus the display metadata a
/// detail view needs (file name/content type) without a second round trip.</summary>
public sealed record AttachedDocumentDownloadUrl(string Url, DateTime ExpiresAt, string FileName, string ContentType);

/// <summary>
/// Başka modüllerin (ör. Leave) talep eki verdiği belgeyi doğrulaması: yalnız <c>Active</c> durumdaki, kiracıya ait
/// belgeler döner. Bulunamayan/silinmiş (Deleted) veya başka kiracının belgeleri sözlükte yer almaz.
/// </summary>
public interface IDocumentAttachmentLookup
{
    Task<IReadOnlyDictionary<Guid, AttachedDocumentInfo>> GetActiveAsync(
        Guid tenantId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default);

    /// <summary>Presigned download URL for an already-attached document (e.g. a leave request's medical report),
    /// reusing the same storage/expiry logic as <c>GET /api/v1/documents/{id}</c>. Returns null when the document
    /// doesn't exist, isn't <c>Active</c>, or belongs to a different tenant - callers should treat that as "not
    /// found" (never distinguish the reason, so existence isn't leaked across tenants/records).</summary>
    Task<AttachedDocumentDownloadUrl?> GetDownloadUrlAsync(Guid tenantId, Guid documentId, CancellationToken ct = default);
}
