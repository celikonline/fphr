namespace Sense.Modules.Documents.Application;

/// <summary>Application layer error codes (localized in Web layer).</summary>
public static class DocumentsErrors
{
    public const string DocumentNotFound = "document.not_found";
    public const string UploadInitiationFailed = "documents.upload_initiation_failed";
    public const string ConfirmationFailed = "documents.confirmation_failed";
    public const string DeletionFailed = "documents.deletion_failed";
    public const string TenantNotAuthorized = "document.tenant_not_authorized";
}
