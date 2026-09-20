namespace Sense.Modules.Documents.Contracts;

/// <summary>Başka modül kaydına (ör. izin talebi) eklenen belgenin özeti: dosya adı talep detayında gösterilir.</summary>
public sealed record AttachedDocumentInfo(Guid DocumentId, string FileName, string ContentType, long Size);

/// <summary>
/// Başka modüllerin (ör. Leave) talep eki verdiği belgeyi doğrulaması: yalnız <c>Active</c> durumdaki, kiracıya ait
/// belgeler döner. Bulunamayan/silinmiş (Deleted) veya başka kiracının belgeleri sözlükte yer almaz.
/// </summary>
public interface IDocumentAttachmentLookup
{
    Task<IReadOnlyDictionary<Guid, AttachedDocumentInfo>> GetActiveAsync(
        Guid tenantId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default);
}
