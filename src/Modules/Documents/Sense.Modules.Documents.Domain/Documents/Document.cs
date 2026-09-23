using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.Documents;

/// <summary>File upload status lifecycle.</summary>
public enum DocumentStatus
{
    /// <summary>Upload initiated, presigned URL provided, awaiting upload completion.</summary>
    Pending = 0,

    /// <summary>File uploaded and confirmed by client; ready for download.</summary>
    Active = 1,

    /// <summary>File deleted but audit records retained.</summary>
    Deleted = 2,
}

/// <summary>
/// Document aggregate root: represents an uploaded file with metadata and audit trail.
/// Tenant isolation: each document belongs to exactly one tenant.
/// </summary>
public sealed class Document : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Document()
    {
    }

    private Document(
        Guid id,
        Guid tenantId,
        string title,
        string fileKey,
        string contentType,
        long size,
        Guid uploadedBy) : base(id, tenantId)
    {
        Title = title;
        FileKey = fileKey;
        ContentType = contentType;
        Size = size;
        UploadedBy = uploadedBy;
        Status = DocumentStatus.Pending;
        UploadedAt = DateTime.UtcNow;
    }

    /// <summary>Document title/display name.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Storage key (path) in MinIO bucket.</summary>
    public string FileKey { get; private set; } = string.Empty;

    /// <summary>MIME type of the document (e.g., "application/pdf").</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; private set; }

    /// <summary>User who uploaded the document.</summary>
    public Guid UploadedBy { get; private set; }

    /// <summary>Timestamp of upload.</summary>
    public DateTime UploadedAt { get; private set; }

    /// <summary>ETag (content hash) from storage for integrity verification.</summary>
    public string? ETag { get; private set; }

    /// <summary>Document status in lifecycle (Pending, Active, Deleted).</summary>
    public DocumentStatus Status { get; private set; }

    /// <summary>Timestamp of deletion, if deleted.</summary>
    public DateTime? DeletedAt { get; private set; }

    /// <summary>User who deleted the document, if applicable.</summary>
    public Guid? DeletedBy { get; private set; }

    /// <summary>Storage bucket name (e.g., "documents", "attachments").</summary>
    public string BucketName { get; private set; } = "documents";

    /// <summary>Validity/expiry date of the document content itself (e.g. driver's license, residence permit, health
    /// report) - distinct from <c>EmployeeDocumentRequirement</c>'s DueDate (which tracks "when must this be
    /// uploaded by", not "until when is the uploaded document valid"). Nullable: most documents have no expiry.</summary>
    public DateOnly? ExpiryDate { get; private set; }

    /// <summary>Employee this document was personally delivered to (e.g. a visa template copy) - null for the
    /// generic tenant-wide bucket most Document rows live in. Distinct from <c>EmployeeDocumentRequirement.DocumentId</c>
    /// (a catalog-tracked requirement); this is a lighter "whose folder is this in" tag for system-delivered copies.</summary>
    public Guid? EmployeeId { get; private set; }

    /// <summary>When this document is a system-delivered copy (e.g. <c>IVisaDocumentDeliveryService</c>), the
    /// <c>VisaDocumentRequest</c> it was delivered for - the audit trail the handoff doc asks for. Null for
    /// ordinarily-uploaded documents.</summary>
    public Guid? VisaDocumentRequestId { get; private set; }

    /// <summary>When this document was generated and delivered by another module (e.g. "Assets" for a zimmet
    /// tutanağı), the producing module's name - generalizes <see cref="VisaDocumentRequestId"/>'s idea to any
    /// module, not only Visa. Null for ordinarily-uploaded documents. See <see cref="SourceRef"/>.</summary>
    public string? SourceModule { get; private set; }

    /// <summary>Paired with <see cref="SourceModule"/>: the producing module's own record id (as a string, since
    /// modules never share entity types across the boundary), e.g. the AssetId a zimmet tutanağı was rendered for.
    /// Together they are the audit trail ("what produced this, and which record") and the idempotency key
    /// <see cref="Sense.Modules.Documents.Domain.IDocumentRepository.FindBySourceAsync"/> looks up - re-delivering
    /// for the same source replaces the previous copy instead of piling up duplicates.</summary>
    public string? SourceRef { get; private set; }

    /// <summary>Creates a new document aggregate with Pending status.</summary>
    public static Result<Document> Create(
        Guid tenantId,
        string title,
        string fileKey,
        string contentType,
        long size,
        Guid uploadedBy,
        string bucketName = "documents")
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("document.title_required");
        }

        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return Error.Validation("document.filekey_required");
        }

        if (size <= 0)
        {
            return Error.Validation("document.invalid_size");
        }

        if (uploadedBy == Guid.Empty)
        {
            return Error.Validation("document.uploader_required");
        }

        var doc = new Document(id: Guid.CreateVersion7(), tenantId, title, fileKey, contentType, size, uploadedBy)
        {
            BucketName = Guard.NotEmpty(bucketName)
        };

        doc.Raise(new DocumentCreated(doc.Id, tenantId, doc.Title, doc.FileKey, size, uploadedBy));
        return doc;
    }

    /// <summary>
    /// Creates a document row for a file the system copied into an employee's folder (e.g.
    /// <c>IVisaDocumentDeliveryService</c> copying a visa template) rather than one a user uploaded through
    /// <see cref="Create"/>. The object already exists in storage under <paramref name="fileKey"/> by the time this
    /// is called, so the row is created directly <see cref="DocumentStatus.Active"/> - there is no pending
    /// presigned-upload step to confirm.
    /// </summary>
    public static Result<Document> CreateDeliveredCopy(
        Guid tenantId,
        Guid employeeId,
        Guid visaDocumentRequestId,
        string title,
        string fileKey,
        string contentType,
        long size,
        string bucketName)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("document.title_required");
        }

        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return Error.Validation("document.filekey_required");
        }

        if (employeeId == Guid.Empty)
        {
            return Error.Validation("document.uploader_required");
        }

        var doc = new Document(
            id: Guid.CreateVersion7(), tenantId, title, fileKey, contentType, size, uploadedBy: Guid.Empty)
        {
            BucketName = Guard.NotEmpty(bucketName),
            EmployeeId = employeeId,
            VisaDocumentRequestId = visaDocumentRequestId,
            Status = DocumentStatus.Active,
        };

        return doc;
    }

    /// <summary>
    /// Creates a document row for a file another module rendered itself (e.g. Assets' zimmet tutanağı PDF) and
    /// already wrote to storage under <paramref name="fileKey"/> - the generic counterpart to
    /// <see cref="CreateDeliveredCopy"/> (which is specific to visa template copies). Created directly
    /// <see cref="DocumentStatus.Active"/>, same reasoning as <see cref="CreateDeliveredCopy"/>: there is no pending
    /// presigned-upload step to confirm.
    /// </summary>
    public static Result<Document> CreateSystemGeneratedCopy(
        Guid tenantId,
        Guid employeeId,
        string sourceModule,
        string sourceRef,
        string title,
        string fileKey,
        string contentType,
        long size,
        string bucketName)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("document.title_required");
        }

        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return Error.Validation("document.filekey_required");
        }

        if (employeeId == Guid.Empty)
        {
            return Error.Validation("document.uploader_required");
        }

        if (string.IsNullOrWhiteSpace(sourceModule) || string.IsNullOrWhiteSpace(sourceRef))
        {
            return Error.Validation("document.source_required");
        }

        return new Document(
            id: Guid.CreateVersion7(), tenantId, title, fileKey, contentType, size, uploadedBy: Guid.Empty)
        {
            BucketName = Guard.NotEmpty(bucketName),
            EmployeeId = employeeId,
            SourceModule = sourceModule,
            SourceRef = sourceRef,
            Status = DocumentStatus.Active,
        };
    }

    /// <summary>Confirms upload: transitions to Active, records ETag.</summary>
    public void ConfirmUpload(string eTag, long actualSize)
    {
        if (Status != DocumentStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot confirm upload for document in {Status} status");
        }

        ETag = Guard.NotEmpty(eTag);
        Size = actualSize;
        Status = DocumentStatus.Active;
        Raise(new DocumentUploadConfirmed(Id, TenantId, eTag, actualSize));
    }

    /// <summary>Sets or clears the document's validity/expiry date. A past date is accepted - an already-expired
    /// document can still be uploaded and will show up in the expiring/expired report immediately.</summary>
    public void SetExpiryDate(DateOnly? expiryDate) => ExpiryDate = expiryDate;

    /// <summary>Marks document as deleted.</summary>
    public void Delete(Guid deletedBy)
    {
        if (Status == DocumentStatus.Deleted)
        {
            throw new InvalidOperationException("Document is already deleted");
        }

        Status = DocumentStatus.Deleted;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        Raise(new DocumentDeleted(Id, TenantId, FileKey, BucketName, deletedBy));
    }
}

/// <summary>Domain event: document was uploaded and awaiting confirmation.</summary>
public sealed record DocumentCreated(
    Guid DocumentId,
    Guid TenantId,
    string Title,
    string FileKey,
    long Size,
    Guid UploadedBy) : DomainEvent;

/// <summary>Domain event: upload confirmation received with ETag.</summary>
public sealed record DocumentUploadConfirmed(
    Guid DocumentId,
    Guid TenantId,
    string ETag,
    long ActualSize) : DomainEvent;

/// <summary>Domain event: document was deleted.</summary>
public sealed record DocumentDeleted(
    Guid DocumentId,
    Guid TenantId,
    string FileKey,
    string BucketName,
    Guid DeletedBy) : DomainEvent;

/// <summary>Filter for the "expiring documents" report (<c>GET /api/v1/documents/expiring</c>): which side of the
/// expiry window a document falls on.</summary>
public enum DocumentExpiryState
{
    /// <summary>ExpiryDate is today or in the future, within the requested window.</summary>
    Expiring = 0,

    /// <summary>ExpiryDate is in the past.</summary>
    Expired = 1,
}
