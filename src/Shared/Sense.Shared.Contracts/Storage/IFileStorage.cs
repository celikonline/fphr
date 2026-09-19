namespace Sense.Shared.Contracts.Storage;

/// <summary>
/// Presigned URL for file uploads. Contains the URL to upload to and metadata about the request.
/// </summary>
public sealed record PresignedUrlRequest(
    string UploadUrl,
    string RequestId,
    DateTime ExpiresAt,
    IDictionary<string, string> Headers);

/// <summary>
/// Presigned URL for file downloads. Contains the URL to download from and expiration info.
/// </summary>
public sealed record PresignedUrlResponse(
    string DownloadUrl,
    DateTime ExpiresAt);

/// <summary>
/// Tenant-isolated file storage abstraction supporting presigned URLs for secure client-side uploads/downloads.
/// Implementations MUST enforce tenant isolation: buckets are namespaced per tenant (e.g., {tenantId}-documents).
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Initiates a file upload by generating a presigned URL valid for client-side uploads.
    /// Tenant isolation: only the specified tenant's bucket is accessible.
    /// </summary>
    /// <param name="tenantId">Tenant identifier; used for bucket naming isolation.</param>
    /// <param name="bucketName">Logical bucket name (e.g., 'documents', 'attachments'). Will be namespaced as {tenantId}-{bucketName}.</param>
    /// <param name="key">Object key (path) within the bucket.</param>
    /// <param name="contentType">MIME type of the file being uploaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Presigned URL request with upload endpoint and headers.</returns>
    Task<PresignedUrlRequest> InitiateUploadAsync(
        Guid tenantId,
        string bucketName,
        string key,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms an upload and validates the ETag (content hash). Called after client completes upload.
    /// Records audit event via outbox for audit trail.
    /// Tenant isolation: ensures only the upload's tenant can confirm.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation verification.</param>
    /// <param name="bucketName">Logical bucket name.</param>
    /// <param name="key">Object key that was uploaded.</param>
    /// <param name="eTag">ETag (content hash) returned by storage after upload completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The object size in bytes as stored (authoritative; never trust a client-reported size).</returns>
    Task<long> ConfirmUploadAsync(
        Guid tenantId,
        string bucketName,
        string key,
        string eTag,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a presigned URL for downloading a file. URL expires after specified minutes.
    /// Tenant isolation: only the specified tenant's bucket is accessible.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation verification.</param>
    /// <param name="bucketName">Logical bucket name.</param>
    /// <param name="key">Object key to download.</param>
    /// <param name="expirationMinutes">URL expiration time in minutes (default 15).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Presigned download URL with expiration.</returns>
    Task<PresignedUrlResponse> GetDownloadUrlAsync(
        Guid tenantId,
        string bucketName,
        string key,
        int expirationMinutes = 15,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from storage. Records audit event.
    /// Tenant isolation: ensures only the file's tenant can delete.
    /// </summary>
    /// <param name="tenantId">Tenant identifier for isolation verification.</param>
    /// <param name="bucketName">Logical bucket name.</param>
    /// <param name="key">Object key to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteFileAsync(
        Guid tenantId,
        string bucketName,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-side copies an existing object to a new key, within the same tenant (e.g. delivering a shared visa
    /// document template into an individual employee's folder without round-tripping the bytes through the API).
    /// Both keys are namespaced under the same tenant-isolated bucket prefix as every other operation here.
    /// </summary>
    /// <param name="tenantId">Tenant identifier; used for bucket naming isolation for both source and destination.</param>
    /// <param name="sourceBucketName">Logical bucket name the source object lives in.</param>
    /// <param name="sourceKey">Object key to copy from.</param>
    /// <param name="destinationBucketName">Logical bucket name to copy into.</param>
    /// <param name="destinationKey">Object key to copy to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CopyObjectAsync(
        Guid tenantId,
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        CancellationToken cancellationToken = default);
}
