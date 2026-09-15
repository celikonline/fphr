namespace Sense.Shared.Infrastructure.Storage;

/// <summary>File storage error codes.</summary>
public static class StorageErrors
{
    public const string BucketNotFound = "storage.bucket_not_found";
    public const string FileNotFound = "storage.file_not_found";
    public const string ETagMismatch = "storage.etag_mismatch";
    public const string TenantIsolationViolation = "storage.tenant_isolation_violation";
    public const string UploadFailed = "storage.upload_failed";
    public const string DeleteFailed = "storage.delete_failed";
    public const string InvalidBucketName = "storage.invalid_bucket_name";
    public const string InvalidKey = "storage.invalid_key";
}
