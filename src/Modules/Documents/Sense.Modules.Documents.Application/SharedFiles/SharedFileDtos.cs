namespace Sense.Modules.Documents.Application.SharedFiles;

/// <summary>Response for the presigned upload URL request (mirrors Documents' InitiateUploadResponse).</summary>
public sealed record InitiateSharedFileUploadResponse(
    Guid FileId,
    string PresignedUrl,
    string RequestId,
    DateTime ExpiresAt);

/// <summary>Response for the confirm-upload endpoint.</summary>
public sealed record ConfirmSharedFileUploadResponse(
    Guid FileId,
    string FileName,
    long Size,
    DateTime UploadedAt);

/// <summary>List item for the shared company files grid.</summary>
public sealed record SharedFileListItemDto(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    DateTime UploadedAt,
    Guid UploadedBy,
    string Visibility,
    IReadOnlyList<string> AllowedRoles);

/// <summary>Single file detail with a presigned download URL.</summary>
public sealed record SharedFileDetailDto(
    Guid Id,
    string FileName,
    string ContentType,
    long Size,
    DateTime UploadedAt,
    string DownloadUrl,
    DateTime DownloadUrlExpiresAt);

/// <summary>Tenant's shared-file storage quota usage (senseik-028 acceptance: "Kullanılan Alan %22 (22.88 MB / 104.86 MB)").</summary>
public sealed record SharedFileQuotaDto(long UsedBytes, long QuotaBytes, double UsedPercent, bool IsExceeded);
