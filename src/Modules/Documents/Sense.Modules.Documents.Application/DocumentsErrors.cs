namespace Sense.Modules.Documents.Application;

/// <summary>Application layer error codes (localized in Web layer).</summary>
public static class DocumentsErrors
{
    public const string DocumentNotFound = "document.not_found";
    public const string UploadInitiationFailed = "documents.upload_initiation_failed";
    public const string ConfirmationFailed = "documents.confirmation_failed";
    public const string DeletionFailed = "documents.deletion_failed";

    /// <summary>Upload rejected: file name empty after sanitisation (only separators/dots/control characters).</summary>
    public const string FileNameInvalid = "documents.file_name_invalid";

    /// <summary>Upload rejected: extension not in the allowlist, or the content type does not match the extension.</summary>
    public const string FileTypeNotAllowed = "documents.file_type_not_allowed";

    /// <summary>Confirm called on a document that is not awaiting upload (already confirmed or deleted).</summary>
    public const string UploadNotPending = "documents.upload_not_pending";

    /// <summary>Storage holds no object (or an empty/over-limit one) for the document being confirmed.</summary>
    public const string UploadedFileInvalid = "documents.uploaded_file_invalid";
    public const string TenantNotAuthorized = "document.tenant_not_authorized";

    /// <summary>Uniqueness check requires a DB round-trip, so it lives here rather than in the Domain (mirrors Employee's EmployeeErrors.CustomFieldKeyTaken).</summary>
    public const string DocumentTypeCodeTaken = "document_type.code_taken";

    public const string NoEmployeeForUser = "documents.employee.not_linked";

    /// <summary>Self-service write attempted against a requirement that belongs to a different employee.</summary>
    public const string NotOwner = "employee_document_requirement.not_owner";
}
