namespace Sense.Modules.Documents.Domain;

/// <summary>Domain error codes for Documents module.</summary>
public static class DocumentsDomainErrors
{
    public const string DocumentNotFound = "document.not_found";
    public const string DocumentTitleRequired = "document.title_required";
    public const string DocumentFileKeyRequired = "document.filekey_required";
    public const string DocumentInvalidSize = "document.invalid_size";
    public const string DocumentUploaderRequired = "document.uploader_required";
    public const string DocumentInvalidStatus = "document.invalid_status";
    public const string DocumentAlreadyDeleted = "document.already_deleted";
    public const string TenantNotAuthorized = "document.tenant_not_authorized";
}

/// <summary>Domain constants and limits.</summary>
public static class DocumentsLimits
{
    public const int TitleMaxLength = 255;
    public const int ContentTypeMaxLength = 100;
    public const long MaxFileSize = 100 * 1024 * 1024; // 100 MB
}
