namespace Sense.Modules.Documents.Application.Documents;

/// <summary>Response for presigned upload URL request.</summary>
public sealed record InitiateUploadResponse(
    Guid DocumentId,
    string PresignedUrl,
    string RequestId,
    DateTime ExpiresAt);

/// <summary>Response for confirm upload endpoint.</summary>
public sealed record ConfirmUploadResponse(
    Guid DocumentId,
    string Title,
    DateTime UploadedAt,
    DateOnly? ExpiryDate = null);

/// <summary>Document detail DTO with presigned download URL.</summary>
public sealed record DocumentDetailDto(
    Guid Id,
    string Title,
    string ContentType,
    long Size,
    DateTime UploadedAt,
    string Status,
    string DownloadUrl,
    DateTime DownloadUrlExpiresAt);

/// <summary>Document list item DTO (for grid/list views).</summary>
public sealed record DocumentListItemDto(
    Guid Id,
    string Title,
    string ContentType,
    long Size,
    DateTime UploadedAt,
    string Status);
