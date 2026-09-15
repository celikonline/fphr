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
}
