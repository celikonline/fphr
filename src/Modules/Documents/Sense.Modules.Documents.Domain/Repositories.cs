using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.SharedFiles;
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

    /// <summary>Documents the system copied into an employee's folder for a visa request (audit trail: <c>Document.VisaDocumentRequestId</c>).</summary>
    Task<IReadOnlyList<Document>> ListByVisaRequestAsync(Guid visaDocumentRequestId, CancellationToken cancellationToken = default);

    /// <summary>The Active system-generated copy previously delivered for this (SourceModule, SourceRef) pair, if
    /// any - the idempotency lookup a cross-module document delivery port uses so re-delivery (e.g. re-running the
    /// AssetAssigned handler after an at-least-once outbox retry) replaces the previous copy instead of creating a
    /// duplicate.</summary>
    Task<Document?> FindBySourceAsync(string sourceModule, string sourceRef, CancellationToken cancellationToken = default);

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

    /// <param name="employeeIds">Null = tüm çalışanlar; dolu ise (veri kapsamı) yalnız bu çalışanların gereksinimleri.</param>
    Task<(IReadOnlyList<EmployeeDocumentRequirement> Items, int Total)> ListReportAsync(
        DocumentRequirementStatus? status, int skip, int take, IReadOnlyCollection<Guid>? employeeIds, CancellationToken cancellationToken = default);

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

/// <summary>Shared company file repository (senseik-028): tenant-wide/role-restricted files, distinct from
/// <see cref="IDocumentRepository"/>'s employee-scoped personnel documents.</summary>
public interface ISharedCompanyFileRepository
{
    void Add(SharedCompanyFile file);

    /// <summary>Gets a file by ID, ensuring it belongs to the current tenant (regardless of visibility - callers
    /// check <see cref="SharedCompanyFile.IsVisibleTo"/> themselves once they have the aggregate).</summary>
    Task<Result<SharedCompanyFile>> GetByIdAsync(Guid fileId, CancellationToken cancellationToken = default);

    /// <summary>Lists all non-deleted files for the current tenant (paginated), newest first. Visibility filtering
    /// against the caller's roles happens in the application layer (mirrors ListDocumentsHandler's tenant-only filter).</summary>
    Task<IReadOnlyList<SharedCompanyFile>> ListByTenantAsync(
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default);

    Task<int> CountByTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>Sum of <see cref="SharedCompanyFile.Size"/> for the tenant's Active (non-deleted, confirmed) files -
    /// the quota usage figure for <c>GET /shared-files/quota</c>.</summary>
    Task<long> SumActiveSizeByTenantAsync(CancellationToken cancellationToken = default);
}

/// <summary>Shared file type catalog repository (E8).</summary>
public interface ISharedFileTypeRepository
{
    void Add(SharedFileType type);

    Task<Result<SharedFileType>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedFileType>> ListAsync(bool? isActive, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive name uniqueness check within the tenant; <paramref name="excludeId"/> skips the row being edited.</summary>
    Task<bool> NameExistsAsync(string name, Guid? excludeId, CancellationToken cancellationToken = default);
}
