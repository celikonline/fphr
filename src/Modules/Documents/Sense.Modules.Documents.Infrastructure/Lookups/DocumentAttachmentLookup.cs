using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;
using Sense.Shared.Contracts.Storage;

namespace Sense.Modules.Documents.Infrastructure.Lookups;

/// <summary><see cref="IDocumentAttachmentLookup"/>: yalnız Active belgeler, tek sorgu (EmployeeLookup deseni).</summary>
public sealed class DocumentAttachmentLookup(DocumentsDbContext db, IFileStorage fileStorage) : IDocumentAttachmentLookup
{
    /// <summary>Matches <c>GetDocumentHandler</c>'s own presigned download URL lifetime.</summary>
    private const int DownloadUrlExpirationMinutes = 15;

    public async Task<IReadOnlyDictionary<Guid, AttachedDocumentInfo>> GetActiveAsync(
        Guid tenantId, IReadOnlyCollection<Guid> documentIds, CancellationToken ct = default)
    {
        if (documentIds.Count == 0)
        {
            return new Dictionary<Guid, AttachedDocumentInfo>();
        }

        return await db.Documents.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Status == DocumentStatus.Active && documentIds.Contains(d.Id))
            .ToDictionaryAsync(
                d => d.Id,
                d => new AttachedDocumentInfo(d.Id, d.Title, d.ContentType, d.Size, d.UploadedBy, d.EmployeeId),
                ct);
    }

    public async Task<AttachedDocumentDownloadUrl?> GetDownloadUrlAsync(Guid tenantId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.TenantId == tenantId && d.Status == DocumentStatus.Active, ct);
        if (doc is null)
        {
            return null;
        }

        var presigned = await fileStorage.GetDownloadUrlAsync(tenantId, doc.BucketName, doc.FileKey, DownloadUrlExpirationMinutes, ct);
        return new AttachedDocumentDownloadUrl(presigned.DownloadUrl, presigned.ExpiresAt, doc.Title, doc.ContentType);
    }
}
