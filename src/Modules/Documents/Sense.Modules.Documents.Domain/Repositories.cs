using Sense.Modules.Documents.Domain.DocumentTypes;
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

    /// <summary>Bulk-resolves <c>ExpiryDate</c> for the given document IDs (tenant-filtered), one query for N requirements
    /// instead of N (used by <c>EmployeeDocumentRequirementMapping.ToDtosAsync</c>). Missing IDs are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, DateOnly?>> GetExpiryDatesByIdsAsync(
        IReadOnlyCollection<Guid> documentIds, CancellationToken cancellationToken = default);
}

/// <summary>Tenant document type catalog repository.</summary>
public interface IDocumentTypeDefinitionRepository
{
    void Add(DocumentTypeDefinition definition);

    Task<Result<DocumentTypeDefinition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentTypeDefinition>> ListAsync(DocumentTypeStage? stage, bool? isActive, CancellationToken cancellationToken = default);

    /// <summary>True once the tenant's catalog has been seeded (any row at all).</summary>
    Task<bool> AnyForTenantAsync(CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>Per-employee document requirement tracker repository.</summary>
public interface IEmployeeDocumentRequirementRepository
{
    void Add(EmployeeDocumentRequirement requirement);

    Task<Result<EmployeeDocumentRequirement>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EmployeeDocumentRequirement?> FindAsync(Guid employeeId, Guid documentTypeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeDocumentRequirement>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmployeeDocumentRequirement> Items, int Total)> ListReportAsync(
        DocumentRequirementStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Paged report of Uploaded requirements whose linked <see cref="Document"/> has a non-null ExpiryDate,
    /// filtered by expiry window (<c>GET /api/v1/documents/expiring</c>). <paramref name="today"/>/<paramref name="withinDays"/>
    /// define the "expiring soon" window; <paramref name="state"/> null = both expiring and already-expired.</summary>
    Task<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)> ListExpiringAsync(
        DateOnly today, int withinDays, DocumentExpiryState? state, int skip, int take, CancellationToken cancellationToken = default);
}

/// <summary>Join row for <see cref="IEmployeeDocumentRequirementRepository.ListExpiringAsync"/>: an Uploaded requirement
/// plus its linked Document's ExpiryDate.</summary>
public sealed record ExpiringDocumentRow(
    Guid RequirementId,
    Guid DocumentId,
    Guid EmployeeId,
    Guid DocumentTypeId,
    DateOnly ExpiryDate);
