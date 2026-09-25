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

    public string? AttachmentFileName { get; private set; }

    public string? AttachmentStorageKey { get; private set; }

    public string? AttachmentContentType { get; private set; }

    public long? AttachmentSizeBytes { get; private set; }

    /// <summary>False between initiate and confirm; a pending attachment is never offered for download.</summary>
    public bool AttachmentConfirmed { get; private set; }

    public bool HasConfirmedAttachment => AttachmentConfirmed && AttachmentStorageKey is not null;

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

    /// <summary>Replaces any previous attachment with a pending one (the caller deletes the old stored object).</summary>
    public Result BeginAttachment(string fileName, string storageKey, string contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > EmployeeNoteLimits.FileNameMaxLength
            || string.IsNullOrWhiteSpace(contentType) || contentType.Length > EmployeeNoteLimits.ContentTypeMaxLength
            || storageKey.Length > EmployeeNoteLimits.StorageKeyMaxLength)
        {
            return Error.Validation(EmployeeNoteErrors.AttachmentInvalid);
        }

        AttachmentFileName = fileName;
        AttachmentStorageKey = storageKey;
        AttachmentContentType = contentType;
        AttachmentSizeBytes = null;
        AttachmentConfirmed = false;
        return Result.Success();
    }

    /// <param name="actualSizeBytes">Size reported by storage after the upload.</param>
    public Result ConfirmAttachment(long actualSizeBytes)
    {
        if (AttachmentStorageKey is null)
        {
            return Error.NotFound(EmployeeNoteErrors.AttachmentNotFound);
        }

        if (actualSizeBytes <= 0 || actualSizeBytes > EmployeeNoteLimits.AttachmentMaxBytes)
        {
            return Error.Rule(EmployeeNoteErrors.AttachmentTooLarge);
        }

        AttachmentSizeBytes = actualSizeBytes;
        AttachmentConfirmed = true;
        return Result.Success();
    }

    public void ClearAttachment()
    {
        AttachmentFileName = null;
        AttachmentStorageKey = null;
        AttachmentContentType = null;
        AttachmentSizeBytes = null;
        AttachmentConfirmed = false;
    }
}

/// <summary>Employee note store. <see cref="ListAsync"/> filters by employee and/or importance, newest first.</summary>
public interface IEmployeeNoteRepository
{
    void Add(EmployeeNote note);

    void Remove(EmployeeNote note);

    Task<EmployeeNote?> GetTrackedAsync(Guid noteId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmployeeNote> Items, long Total)> ListAsync(
        Guid? employeeId, EmployeeNoteImportance? importance, int skip, int take, CancellationToken cancellationToken = default);
}
