using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Domain.SharedFiles;

/// <summary>Upload lifecycle, mirrors <c>Document.DocumentStatus</c> (presigned-URL flow: Pending until the client
/// confirms the upload, then Active; Deleted keeps the row for audit).</summary>
public enum SharedFileStatus
{
    Pending = 0,
    Active = 1,
    Deleted = 2,
}

/// <summary>Who can see/download a shared company file. senseik-028 MVP covers tenant-wide and role-restricted;
/// org-unit-restricted visibility is intentionally out of scope for this pass (see handoff doc).</summary>
public enum SharedFileVisibility
{
    /// <summary>Every tenant member with <c>documents.company.view</c> can see this file.</summary>
    Everyone = 0,

    /// <summary>Only callers whose <see cref="Sense.Shared.Contracts.Context.ICurrentUser.Roles"/> intersects
    /// <see cref="SharedCompanyFile.AllowedRoles"/> can see this file.</summary>
    RestrictedToRoles = 1,
}

/// <summary>
/// Shared company file aggregate root: a tenant-wide (or role-restricted) file, distinct from <see cref="Documents.Document"/>
/// which is always tied to one employee's personnel document lifecycle (senseik-028: "ortak şirket dosya paylaşımı").
/// Reuses the same presigned-URL storage flow (<c>IFileStorage</c>) and bucket-per-tenant isolation as Document,
/// under its own logical bucket ("shared-files") so its objects never mix with employee personnel documents.
/// </summary>
public sealed class SharedCompanyFile : TenantAggregateRoot<Guid>, IAuditLogged
{
    /// <summary>Logical storage bucket for shared company files (namespaced per tenant by IFileStorage, like Document's "documents" bucket).</summary>
    public const string BucketName = "shared-files";

    private SharedCompanyFile()
    {
    }

    private SharedCompanyFile(
        Guid id,
        Guid tenantId,
        string fileName,
        string fileKey,
        string contentType,
        long size,
        Guid uploadedBy,
        SharedFileVisibility visibility,
        IReadOnlyCollection<string> allowedRoles) : base(id, tenantId)
    {
        FileName = fileName;
        FileKey = fileKey;
        ContentType = contentType;
        Size = size;
        UploadedBy = uploadedBy;
        Status = SharedFileStatus.Pending;
        UploadedAt = DateTime.UtcNow;
        Visibility = visibility;
        AllowedRoles = allowedRoles.ToList().AsReadOnly();
    }

    /// <summary>Display file name (e.g. "Personel Yönetmeliği.pdf").</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>Storage key (path) in the tenant's shared-files bucket.</summary>
    public string FileKey { get; private set; } = string.Empty;

    /// <summary>MIME type of the file.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>File size in bytes; updated with the confirmed actual size once the upload completes.</summary>
    public long Size { get; private set; }

    /// <summary>User who uploaded the file.</summary>
    public Guid UploadedBy { get; private set; }

    /// <summary>Timestamp of upload initiation.</summary>
    public DateTime UploadedAt { get; private set; }

    /// <summary>ETag (content hash) from storage, recorded on confirmation.</summary>
    public string? ETag { get; private set; }

    public SharedFileStatus Status { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public SharedFileVisibility Visibility { get; private set; }

    /// <summary>Role names allowed to see this file when <see cref="Visibility"/> is <see cref="SharedFileVisibility.RestrictedToRoles"/>;
    /// empty when <see cref="SharedFileVisibility.Everyone"/>. Fixed at creation - MVP has no "edit visibility" use case.</summary>
    public IReadOnlyList<string> AllowedRoles { get; private set; } = new List<string>().AsReadOnly();

    /// <summary>Optional <see cref="SharedFileType"/> (E8 catalog); null = untyped.</summary>
    public Guid? TypeId { get; private set; }

    /// <summary>Whether the mobile app lists this file (E8 "mobilde görünsün"); defaults from the type at upload.</summary>
    public bool VisibleOnMobile { get; private set; }

    /// <summary>Sets the E8 classification: type and mobile visibility.</summary>
    public void Classify(Guid? typeId, bool visibleOnMobile)
    {
        TypeId = typeId;
        VisibleOnMobile = visibleOnMobile;
    }

    /// <summary>Creates a new shared file aggregate in Pending status, awaiting upload confirmation.</summary>
    public static Result<SharedCompanyFile> Create(
        Guid tenantId,
        string fileName,
        string fileKey,
        string contentType,
        long size,
        Guid uploadedBy,
        SharedFileVisibility visibility = SharedFileVisibility.Everyone,
        IReadOnlyCollection<string>? allowedRoles = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Error.Validation(SharedFilesDomainErrors.NameRequired);
        }

        if (fileName.Length > SharedFilesLimits.FileNameMaxLength)
        {
            return Error.Validation(SharedFilesDomainErrors.NameTooLong);
        }

        if (string.IsNullOrWhiteSpace(fileKey))
        {
            return Error.Validation(SharedFilesDomainErrors.FileKeyRequired);
        }

        if (size <= 0 || size > SharedFilesLimits.MaxFileSize)
        {
            return Error.Validation(SharedFilesDomainErrors.InvalidSize);
        }

        if (uploadedBy == Guid.Empty)
        {
            return Error.Validation(SharedFilesDomainErrors.UploaderRequired);
        }

        var roles = (allowedRoles ?? []).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (visibility == SharedFileVisibility.RestrictedToRoles && roles.Count == 0)
        {
            return Error.Validation(SharedFilesDomainErrors.RolesRequiredForRestrictedVisibility);
        }

        var file = new SharedCompanyFile(
            Guid.CreateVersion7(), tenantId, fileName, fileKey, contentType, size, uploadedBy, visibility,
            visibility == SharedFileVisibility.RestrictedToRoles ? roles : []);

        file.Raise(new SharedCompanyFileCreated(file.Id, tenantId, file.FileName, file.FileKey, size, uploadedBy));
        return file;
    }

    /// <summary>Confirms upload: transitions to Active, records ETag and the actual uploaded size.</summary>
    public void ConfirmUpload(string eTag, long actualSize)
    {
        if (Status != SharedFileStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot confirm upload for shared file in {Status} status");
        }

        ETag = Guard.NotEmpty(eTag);
        Size = actualSize;
        Status = SharedFileStatus.Active;
        Raise(new SharedCompanyFileUploadConfirmed(Id, TenantId, eTag, actualSize));
    }

    /// <summary>Marks the file as deleted (soft-delete, audit row retained).</summary>
    public void Delete(Guid deletedBy)
    {
        if (Status == SharedFileStatus.Deleted)
        {
            throw new InvalidOperationException("Shared file is already deleted");
        }

        Status = SharedFileStatus.Deleted;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        Raise(new SharedCompanyFileDeleted(Id, TenantId, FileKey, deletedBy));
    }

    /// <summary>Visibility rule: tenant-wide files are visible to anyone with the view permission; role-restricted
    /// files require at least one of the caller's roles to be in <see cref="AllowedRoles"/> (case-insensitive).</summary>
    public bool IsVisibleTo(IReadOnlySet<string> callerRoles) =>
        Visibility == SharedFileVisibility.Everyone
        || AllowedRoles.Any(allowed => callerRoles.Contains(allowed, StringComparer.OrdinalIgnoreCase));
}

/// <summary>Domain event: a shared company file was uploaded and awaits confirmation.</summary>
public sealed record SharedCompanyFileCreated(
    Guid FileId,
    Guid TenantId,
    string FileName,
    string FileKey,
    long Size,
    Guid UploadedBy) : DomainEvent;

/// <summary>Domain event: upload confirmation received with ETag.</summary>
public sealed record SharedCompanyFileUploadConfirmed(
    Guid FileId,
    Guid TenantId,
    string ETag,
    long ActualSize) : DomainEvent;

/// <summary>Domain event: shared company file was deleted.</summary>
public sealed record SharedCompanyFileDeleted(
    Guid FileId,
    Guid TenantId,
    string FileKey,
    Guid DeletedBy) : DomainEvent;
