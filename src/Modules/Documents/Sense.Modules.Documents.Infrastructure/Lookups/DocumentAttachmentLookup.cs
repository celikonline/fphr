using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Infrastructure.Persistence;

namespace Sense.Modules.Documents.Infrastructure.Lookups;

/// <summary><see cref="IDocumentAttachmentLookup"/>: yalnız Active belgeler, tek sorgu (EmployeeLookup deseni).</summary>
public sealed class DocumentAttachmentLookup(DocumentsDbContext db) : IDocumentAttachmentLookup
{
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
                d => new AttachedDocumentInfo(d.Id, d.Title, d.ContentType, d.Size),
                ct);
    }
}
