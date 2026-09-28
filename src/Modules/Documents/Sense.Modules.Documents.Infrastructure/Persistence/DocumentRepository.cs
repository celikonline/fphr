using Microsoft.EntityFrameworkCore;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Infrastructure.Persistence;

/// <summary>Document repository implementation with tenant isolation.</summary>
public sealed class DocumentRepository(DocumentsDbContext context) : IDocumentRepository
{
    public void Add(Document document)
    {
        context.Documents.Add(document);
    }

    public async Task<Result<Document>> GetByIdAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        // Query is automatically filtered by tenant via global filter in DbContext
        var document = await context.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            return Error.NotFound(DocumentsDomainErrors.DocumentNotFound);
        }

        return document;
    }

    public async Task<IReadOnlyList<Document>> ListByTenantAsync(
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        // Query is automatically filtered by tenant via global filter in DbContext. System-generated deliveries
        // (visa copies, SourceModule-tagged copies like Assets' handover form) live in the employee's own folder,
        // not the generic tenant-wide list - same treatment for both, mirroring the existing VisaDocumentRequestId exclusion.
        var documents = await context.Documents
            .Where(d => d.Status != DocumentStatus.Deleted && d.VisaDocumentRequestId == null && d.SourceModule == null)
            .OrderByDescending(d => d.UploadedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return documents.AsReadOnly();
    }

    public async Task<IReadOnlyList<Document>> ListByVisaRequestAsync(
        Guid visaDocumentRequestId, CancellationToken cancellationToken = default) =>
        await context.Documents
            .AsNoTracking()
            .Where(d => d.VisaDocumentRequestId == visaDocumentRequestId && d.Status != DocumentStatus.Deleted)
            .OrderBy(d => d.Title)
            .ToListAsync(cancellationToken);

    public async Task<Document?> FindBySourceAsync(
        string sourceModule, string sourceRef, CancellationToken cancellationToken = default) =>
        await context.Documents
            .Where(d => d.SourceModule == sourceModule && d.SourceRef == sourceRef && d.Status != DocumentStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int> CountByTenantAsync(CancellationToken cancellationToken = default)
    {
        // Query is automatically filtered by tenant via global filter in DbContext
        return await context.Documents
            .Where(d => d.Status != DocumentStatus.Deleted && d.VisaDocumentRequestId == null && d.SourceModule == null)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> ExistsByKeyAsync(
        string fileKey,
        string bucketName,
        CancellationToken cancellationToken = default)
    {
        // Query is automatically filtered by tenant via global filter in DbContext
        return await context.Documents
            .Where(d => d.Status != DocumentStatus.Deleted)
            .AnyAsync(d => d.FileKey == fileKey && d.BucketName == bucketName, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, DateOnly?>> GetExpiryDatesByIdsAsync(
        IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default)
    {
        if (documentIds.Count == 0)
        {
            return new Dictionary<Guid, DateOnly?>();
        }

        // Query is automatically filtered by tenant via global filter in DbContext
        return await context.Documents
            .Where(d => documentIds.Contains(d.Id))
            .Select(d => new { d.Id, d.ExpiryDate })
            .ToDictionaryAsync(d => d.Id, d => d.ExpiryDate, cancellationToken);
    }
}
