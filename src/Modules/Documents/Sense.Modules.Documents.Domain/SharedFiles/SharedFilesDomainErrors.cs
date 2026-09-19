namespace Sense.Modules.Documents.Domain.SharedFiles;

/// <summary>Domain error codes for the shared company files sub-area of the Documents module.</summary>
public static class SharedFilesDomainErrors
{
    public const string NotFound = "shared_company_file.not_found";
    public const string NameRequired = "shared_company_file.name_required";
    public const string NameTooLong = "shared_company_file.name_too_long";
    public const string FileKeyRequired = "shared_company_file.filekey_required";
    public const string InvalidSize = "shared_company_file.invalid_size";
    public const string UploaderRequired = "shared_company_file.uploader_required";
    public const string RolesRequiredForRestrictedVisibility = "shared_company_file.roles_required_for_restricted_visibility";
    public const string InvalidStatus = "shared_company_file.invalid_status";
    public const string AlreadyDeleted = "shared_company_file.already_deleted";

    /// <summary>Uploading the file (at its declared size) would push the tenant's shared-file storage usage past its quota.</summary>
    public const string QuotaExceeded = "shared_company_file.quota_exceeded";
}

/// <summary>Domain constants and limits for shared company files.</summary>
public static class SharedFilesLimits
{
    public const int FileNameMaxLength = 255;
    public const long MaxFileSize = 100 * 1024 * 1024; // 100 MB per file (mirrors Document's DocumentsLimits.MaxFileSize)

    /// <summary>
    /// Default per-tenant storage quota for the shared company files area (senseik-028 acceptance: "Kullanılan Alan
    /// %22 (22.88 MB / 104.86 MB)" - 104.86 MB ~= 100 MiB). MVP ships this as a single tenant-wide constant rather
    /// than a configurable per-tenant setting (no TenantSettings numeric-value infrastructure exists yet to hang a
    /// real `SharedFileQuotaBytes` setting off of - see the handoff doc for the follow-up).
    /// </summary>
    public const long DefaultQuotaBytes = 100L * 1024 * 1024;
}
