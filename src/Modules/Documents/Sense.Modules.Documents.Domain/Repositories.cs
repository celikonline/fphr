using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain;

/// <summary>Document aggregate repository.</summary>
public interface IDocumentRepository
{
    /// <summary>Adds a new document to the repository.</summary>
    void Add(Document document);

    /// <summary>Gets a document by ID, ensuring it belongs to the current tenant.</summary>
    Task<Result<Document>> GetByIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Gets all documents for the current tenant (paginated).</summary>
    Task<IReadOnlyList<Document>> ListByTenantAsync(
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Counts documents for the current tenant.</summary>
    Task<int> CountByTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks if a document with given key already exists in bucket.</summary>
    Task<bool> ExistsByKeyAsync(string fileKey, string bucketName, CancellationToken cancellationToken = default);
}
