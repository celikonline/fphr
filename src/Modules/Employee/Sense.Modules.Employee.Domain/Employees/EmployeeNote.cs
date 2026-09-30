using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

public enum EmployeeNoteImportance
{
    Low = 0,
    Normal = 1,
    High = 2,
}

public static class EmployeeNoteLimits
{
    public const int TitleMaxLength = 255;
    public const int ContentMaxLength = 512;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 127;
    public const int StorageKeyMaxLength = 512;
    public const int AuthorNameMaxLength = 200;

    /// <summary>Attachment size cap (10 MB); the size stored is the storage-reported one, never the client's.</summary>
    public const long AttachmentMaxBytes = 10L * 1024 * 1024;
}

public static class EmployeeNoteErrors
{
    public const string NotFound = "employee.note_not_found";
    public const string TitleRequired = "employee.note_title_required";
    public const string TitleTooLong = "employee.note_title_too_long";
    public const string ContentTooLong = "employee.note_content_too_long";
    public const string ImportanceInvalid = "employee.note_importance_invalid";
    public const string AttachmentInvalid = "employee.note_attachment_invalid";
    public const string AttachmentTooLarge = "employee.note_attachment_too_large";
    public const string AttachmentNotFound = "employee.note_attachment_not_found";
    public const string AttachmentTypeNotAllowed = "employee.note_attachment_type_not_allowed";
}

/// <summary>
/// HR/manager note on an employee card (E7): short title + content, an importance level, the author who wrote it and at
/// most one attachment uploaded through the presigned <see cref="Sense.Shared.Contracts.Storage.IFileStorage"/> flow
/// (initiate -> client PUT -> confirm). Readable only with employee.notes.view, never through the self-service (my) permissions.
/// </summary>
public sealed class EmployeeNote : TenantAggregateRoot<Guid>, IAuditLogged
{
    public const string BucketName = "employee-note-attachments";

    private EmployeeNote()
    {
    }

    private EmployeeNote(Guid id, Guid tenantId)
        : base(id, tenantId)
    {
    }

    public Guid EmployeeId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public EmployeeNoteImportance Importance { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public string? AuthorName { get; private set; }

    /// <summary>The confirmed attachment (all three are set together or all null); only this one is ever offered for download.</summary>
    public string? AttachmentFileName { get; private set; }

    public string? AttachmentStorageKey { get; private set; }

    public string? AttachmentContentType { get; private set; }

    public long? AttachmentSizeBytes { get; private set; }

    /// <summary>Upload started but not confirmed yet; it replaces the confirmed attachment only on <see cref="ConfirmAttachment"/>.</summary>
    public string? PendingAttachmentFileName { get; private set; }

    public string? PendingAttachmentStorageKey { get; private set; }

    public string? PendingAttachmentContentType { get; private set; }

    public bool HasConfirmedAttachment => AttachmentStorageKey is not null;

    public static Result<EmployeeNote> Create(
        Guid tenantId, Guid employeeId, string? title, string? content, EmployeeNoteImportance importance, Guid authorUserId, string? authorName)
    {
        var note = new EmployeeNote(Guid.CreateVersion7(), tenantId)
        {
            EmployeeId = employeeId,
            AuthorUserId = authorUserId,
            AuthorName = authorName?.Trim() is { Length: > 0 } name
                ? name[..Math.Min(name.Length, EmployeeNoteLimits.AuthorNameMaxLength)]
                : null,
        };
        var applied = note.Update(title, content, importance);
        return applied.IsFailure ? applied.Error : note;
    }

    public Result Update(string? title, string? content, EmployeeNoteImportance importance)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        var trimmedContent = content?.Trim() ?? string.Empty;
        if (trimmedTitle.Length == 0)
        {
            return Error.Validation(EmployeeNoteErrors.TitleRequired);
        }

        if (trimmedTitle.Length > EmployeeNoteLimits.TitleMaxLength)
        {
            return Error.Validation(EmployeeNoteErrors.TitleTooLong);
        }

        if (trimmedContent.Length > EmployeeNoteLimits.ContentMaxLength)
        {
            return Error.Validation(EmployeeNoteErrors.ContentTooLong);
        }

        if (!Enum.IsDefined(importance))
        {
            return Error.Validation(EmployeeNoteErrors.ImportanceInvalid);
        }

        Title = trimmedTitle;
        Content = trimmedContent;
        Importance = importance;
        return Result.Success();
    }

    /// <summary>Starts a new upload without touching the confirmed attachment; returns the superseded pending key (orphaned storage) or null.</summary>
    public Result<ReplacedStorageKey> BeginAttachment(string fileName, string storageKey, string contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > EmployeeNoteLimits.FileNameMaxLength
            || string.IsNullOrWhiteSpace(contentType) || contentType.Length > EmployeeNoteLimits.ContentTypeMaxLength
            || storageKey.Length > EmployeeNoteLimits.StorageKeyMaxLength)
        {
            return Error.Validation(EmployeeNoteErrors.AttachmentInvalid);
        }

        var superseded = PendingAttachmentStorageKey;
        PendingAttachmentFileName = fileName;
        PendingAttachmentStorageKey = storageKey;
        PendingAttachmentContentType = contentType;
        return new ReplacedStorageKey(superseded);
    }

    /// <param name="actualSizeBytes">Size reported by storage after the upload.</param>
    /// <returns>The storage key of the previous confirmed attachment (to delete after commit), or null.</returns>
    public Result<ReplacedStorageKey> ConfirmAttachment(long actualSizeBytes)
    {
        if (PendingAttachmentStorageKey is null)
        {
            return Error.NotFound(EmployeeNoteErrors.AttachmentNotFound);
        }

        if (actualSizeBytes <= 0 || actualSizeBytes > EmployeeNoteLimits.AttachmentMaxBytes)
        {
            return Error.Rule(EmployeeNoteErrors.AttachmentTooLarge);
        }

        var previous = AttachmentStorageKey;
        AttachmentFileName = PendingAttachmentFileName;
        AttachmentStorageKey = PendingAttachmentStorageKey;
        AttachmentContentType = PendingAttachmentContentType;
        AttachmentSizeBytes = actualSizeBytes;
        PendingAttachmentFileName = null;
        PendingAttachmentStorageKey = null;
        PendingAttachmentContentType = null;
        return new ReplacedStorageKey(previous);
    }

    /// <summary>Removes the confirmed and any pending attachment; returns their storage keys (to delete after commit).</summary>
    public IReadOnlyList<string> ClearAttachment()
    {
        var keys = StorageKeys();
        AttachmentFileName = null;
        AttachmentStorageKey = null;
        AttachmentContentType = null;
        AttachmentSizeBytes = null;
        PendingAttachmentFileName = null;
        PendingAttachmentStorageKey = null;
        PendingAttachmentContentType = null;
        return keys;
    }

    public IReadOnlyList<string> StorageKeys() =>
        new[] { AttachmentStorageKey, PendingAttachmentStorageKey }.Where(k => k is not null).Select(k => k!).ToList();
}

/// <summary>Storage key of an object that stopped being referenced (null when nothing was replaced); a wrapper because Result&lt;T&gt; cannot carry null.</summary>
public sealed record ReplacedStorageKey(string? Key);

/// <summary>Employee note store. <see cref="ListAsync"/> filters by employee and/or importance, newest first.</summary>
public interface IEmployeeNoteRepository
{
    void Add(EmployeeNote note);

    void Remove(EmployeeNote note);

    Task<EmployeeNote?> GetTrackedAsync(Guid noteId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmployeeNote> Items, long Total)> ListAsync(
        Guid? employeeId, EmployeeNoteImportance? importance, int skip, int take, CancellationToken cancellationToken = default);
}
