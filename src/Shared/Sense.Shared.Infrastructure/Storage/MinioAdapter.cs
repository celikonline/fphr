using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Infrastructure.Observability;
using Sense.Shared.Kernel;

namespace Sense.Shared.Infrastructure.Storage;

/// <summary>Configuration for MinIO storage provider.</summary>
public sealed class MinioOptions
{
    public const string Section = "MinIO";

    /// <summary>MinIO server endpoint used for server-to-server calls (e.g., "minio:9000" inside Docker).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint embedded in presigned URLs handed to browser/mobile clients (e.g., "localhost:9000").
    /// Falls back to <see cref="Endpoint"/> when unset — needed whenever <see cref="Endpoint"/> is a
    /// Docker-internal hostname that isn't resolvable outside the compose network.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    /// <summary>MinIO access key.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>MinIO secret key.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Use HTTPS for MinIO communication (default false for local dev).</summary>
    public bool UseHttps { get; set; }

    /// <summary>Use HTTPS for the public endpoint. Falls back to <see cref="UseHttps"/> when unset.</summary>
    public bool? PublicUseHttps { get; set; }

    /// <summary>Default upload presigned URL expiration in minutes (default 60).</summary>
    public int UploadExpirationMinutes { get; set; } = 60;

    /// <summary>Default download presigned URL expiration in minutes (default 15).</summary>
    public int DownloadExpirationMinutes { get; set; } = 15;
}

/// <summary>MinIO implementation of IFileStorage with tenant isolation via bucket naming.</summary>
public sealed class MinioAdapter : IFileStorage
{
    private const int SecondsPerMinute = 60;
    private const int MaxDownloadExpirationMinutes = 7 * 24 * 60; // Max 7 days
    private const int TenantPrefixLength = 12; // First 12 chars of the tenant id (N format)
    private const string RequestIdFormat = "N";
    private const string ContentTypeHeaderName = "Content-Type";

    private readonly IMinioClient _client;
    private readonly IMinioClient _publicClient;
    private readonly MinioOptions _options;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<MinioAdapter> _logger;

    public MinioAdapter(
        IMinioClient client,
        [FromKeyedServices(PublicClientKey)] IMinioClient publicClient,
        IOptions<MinioOptions> options,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<MinioAdapter> logger)
    {
        _client = Guard.NotNull(client);
        _publicClient = Guard.NotNull(publicClient);
        _options = Guard.NotNull(options).Value;
        _tenantContext = Guard.NotNull(tenantContext);
        _currentUser = Guard.NotNull(currentUser);
        _logger = Guard.NotNull(logger);
    }

    /// <summary>DI key for the MinIO client used to sign URLs handed to external clients.</summary>
    public const string PublicClientKey = "minio-public";

    /// <inheritdoc />
    public async Task<PresignedUrlRequest> InitiateUploadAsync(
        Guid tenantId,
        string bucketName,
        string key,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartStorageActivity("document.upload.initiate", tenantId, bucketName, key);

        ValidateParameters(tenantId, bucketName, key);

        var isolatedBucket = CreateBucketName(tenantId, bucketName);

        try
        {
            // Ensure bucket exists
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(isolatedBucket);
            var bucketExists = await _client.BucketExistsAsync(bucketExistsArgs, cancellationToken);

            if (!bucketExists)
            {
                var makeBucketArgs = new MakeBucketArgs().WithBucket(isolatedBucket);
                await _client.MakeBucketAsync(makeBucketArgs, cancellationToken);
                _logger.LogInformation("Created bucket {Bucket} for tenant {TenantId}", isolatedBucket, tenantId);
            }

            // Generate presigned upload URL
            var presignedArgs = new PresignedPutObjectArgs()
                .WithBucket(isolatedBucket)
                .WithObject(key)
                .WithExpiry(_options.UploadExpirationMinutes * SecondsPerMinute); // Convert to seconds

            var uploadUrl = await _publicClient.PresignedPutObjectAsync(presignedArgs);
            var expiresAt = DateTime.UtcNow.AddMinutes(_options.UploadExpirationMinutes);
            var requestId = Guid.CreateVersion7().ToString(RequestIdFormat);

            _logger.LogInformation(
                "Generated presigned upload URL for {TenantId}/{BucketName}/{Key}, expires at {ExpiresAt}",
                tenantId, bucketName, key, expiresAt);

            return new PresignedUrlRequest(
                uploadUrl,
                requestId,
                expiresAt,
                new Dictionary<string, string>
                {
                    { ContentTypeHeaderName, contentType }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to initiate upload for {TenantId}/{BucketName}/{Key}",
                tenantId, bucketName, key);
            throw new InvalidOperationException($"Failed to initiate upload: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<long> ConfirmUploadAsync(
        Guid tenantId,
        string bucketName,
        string key,
        string eTag,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartStorageActivity("document.upload.confirm", tenantId, bucketName, key);

        ValidateParameters(tenantId, bucketName, key);
        Guard.NotEmpty(eTag, nameof(eTag));

        var isolatedBucket = CreateBucketName(tenantId, bucketName);

        try
        {
            // Verify object exists and check ETag
            var statArgs = new StatObjectArgs()
                .WithBucket(isolatedBucket)
                .WithObject(key);

            var stat = await _client.StatObjectAsync(statArgs, cancellationToken);

            // Normalize ETags (MinIO might return quoted ETags)
            var normalizedExpectedETag = eTag.Trim('"');
            var normalizedActualETag = stat.ETag.Trim('"');

            if (!normalizedActualETag.Equals(normalizedExpectedETag, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "ETag mismatch for {TenantId}/{BucketName}/{Key}: expected {ExpectedETag}, got {ActualETag}",
                    tenantId, bucketName, key, normalizedExpectedETag, normalizedActualETag);
                throw new StorageETagMismatchException("Upload confirmation failed: ETag mismatch");
            }

            _logger.LogInformation(
                "Confirmed upload for {TenantId}/{BucketName}/{Key} (ETag: {ETag}, Size: {Size} bytes)",
                tenantId, bucketName, key, stat.ETag, stat.Size);

            // TODO: Write audit event to outbox (Item 17 will integrate with audit system)

            return stat.Size;
        }
        catch (Exception ex) when (IsObjectNotFound(ex))
        {
            _logger.LogError("Object not found for confirmation: {TenantId}/{BucketName}/{Key}", tenantId, bucketName, key);
            throw new StorageObjectNotFoundException("File not found for confirmation", ex);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex,
                "Failed to confirm upload for {TenantId}/{BucketName}/{Key}",
                tenantId, bucketName, key);
            throw new InvalidOperationException($"Failed to confirm upload: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<PresignedUrlResponse> GetDownloadUrlAsync(
        Guid tenantId,
        string bucketName,
        string key,
        int expirationMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartStorageActivity("document.download.url", tenantId, bucketName, key);

        ValidateParameters(tenantId, bucketName, key);

        if (expirationMinutes < 1 || expirationMinutes > MaxDownloadExpirationMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(expirationMinutes), $"Must be between 1 and {MaxDownloadExpirationMinutes} minutes");
        }

        var isolatedBucket = CreateBucketName(tenantId, bucketName);

        try
        {
            // Verify object exists
            var statArgs = new StatObjectArgs()
                .WithBucket(isolatedBucket)
                .WithObject(key);

            await _client.StatObjectAsync(statArgs, cancellationToken);

            // Generate presigned download URL
            var presignedArgs = new PresignedGetObjectArgs()
                .WithBucket(isolatedBucket)
                .WithObject(key)
                .WithExpiry(expirationMinutes * SecondsPerMinute); // Convert to seconds

            var downloadUrl = await _publicClient.PresignedGetObjectAsync(presignedArgs);
            var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

            _logger.LogInformation(
                "Generated presigned download URL for {TenantId}/{BucketName}/{Key}, expires at {ExpiresAt}",
                tenantId, bucketName, key, expiresAt);

            return new PresignedUrlResponse(downloadUrl, expiresAt);
        }
        catch (Exception ex) when (IsObjectNotFound(ex))
        {
            _logger.LogWarning("Object not found for download: {TenantId}/{BucketName}/{Key}", tenantId, bucketName, key);
            throw new InvalidOperationException("File not found", ex);
        }
        catch (Exception ex) when (!(ex is ArgumentOutOfRangeException || ex is InvalidOperationException))
        {
            _logger.LogError(ex,
                "Failed to generate download URL for {TenantId}/{BucketName}/{Key}",
                tenantId, bucketName, key);
            throw new InvalidOperationException($"Failed to generate download URL: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task DeleteFileAsync(
        Guid tenantId,
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartStorageActivity("document.delete", tenantId, bucketName, key);

        ValidateParameters(tenantId, bucketName, key);

        var isolatedBucket = CreateBucketName(tenantId, bucketName);

        try
        {
            var removeArgs = new RemoveObjectArgs()
                .WithBucket(isolatedBucket)
                .WithObject(key);

            await _client.RemoveObjectAsync(removeArgs, cancellationToken);

            _logger.LogInformation(
                "Deleted file {TenantId}/{BucketName}/{Key} by user {UserId}",
                tenantId, bucketName, key, _currentUser.UserId);

            // TODO: Write audit event to outbox (Item 17 will integrate with audit system)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to delete file {TenantId}/{BucketName}/{Key}",
                tenantId, bucketName, key);
            throw new InvalidOperationException($"Failed to delete file: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task CopyObjectAsync(
        Guid tenantId,
        string sourceBucketName,
        string sourceKey,
        string destinationBucketName,
        string destinationKey,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartStorageActivity("document.copy", tenantId, destinationBucketName, destinationKey);

        ValidateParameters(tenantId, sourceBucketName, sourceKey);
        ValidateParameters(tenantId, destinationBucketName, destinationKey);

        // Source and destination are always the same tenant (see the interface doc) - both isolate under the same prefix.
        var isolatedSourceBucket = CreateBucketName(tenantId, sourceBucketName);
        var isolatedDestinationBucket = CreateBucketName(tenantId, destinationBucketName);

        try
        {
            var destinationBucketExists = await _client.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(isolatedDestinationBucket), cancellationToken);
            if (!destinationBucketExists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(isolatedDestinationBucket), cancellationToken);
                _logger.LogInformation("Created bucket {Bucket} for tenant {TenantId}", isolatedDestinationBucket, tenantId);
            }

            var copySource = new CopySourceObjectArgs()
                .WithBucket(isolatedSourceBucket)
                .WithObject(sourceKey);

            var copyArgs = new CopyObjectArgs()
                .WithBucket(isolatedDestinationBucket)
                .WithObject(destinationKey)
                .WithCopyObjectSource(copySource);

            await _client.CopyObjectAsync(copyArgs, cancellationToken);

            _logger.LogInformation(
                "Copied {TenantId}/{SourceBucket}/{SourceKey} to {TenantId}/{DestinationBucket}/{DestinationKey}",
                tenantId, sourceBucketName, sourceKey, tenantId, destinationBucketName, destinationKey);
        }
        catch (Exception ex) when (IsObjectNotFound(ex))
        {
            _logger.LogError("Object not found for copy: {TenantId}/{SourceBucket}/{SourceKey}", tenantId, sourceBucketName, sourceKey);
            throw new InvalidOperationException("File not found for copy", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException and not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Failed to copy {TenantId}/{SourceBucket}/{SourceKey} to {DestinationBucket}/{DestinationKey}",
                tenantId, sourceBucketName, sourceKey, destinationBucketName, destinationKey);
            throw new InvalidOperationException($"Failed to copy file: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task PutObjectAsync(
        Guid tenantId,
        string bucketName,
        string key,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartStorageActivity("document.put", tenantId, bucketName, key);

        ValidateParameters(tenantId, bucketName, key);
        Guard.NotEmpty(contentType, nameof(contentType));
        Guard.NotNull(content, nameof(content));

        var isolatedBucket = CreateBucketName(tenantId, bucketName);

        try
        {
            var bucketExists = await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(isolatedBucket), cancellationToken);
            if (!bucketExists)
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(isolatedBucket), cancellationToken);
                _logger.LogInformation("Created bucket {Bucket} for tenant {TenantId}", isolatedBucket, tenantId);
            }

            using var stream = new MemoryStream(content, writable: false);
            var putArgs = new PutObjectArgs()
                .WithBucket(isolatedBucket)
                .WithObject(key)
                .WithStreamData(stream)
                .WithObjectSize(content.Length)
                .WithContentType(contentType);

            await _client.PutObjectAsync(putArgs, cancellationToken);

            _logger.LogInformation(
                "Wrote {TenantId}/{BucketName}/{Key} ({Size} bytes)", tenantId, bucketName, key, content.Length);
        }
        catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to write {TenantId}/{BucketName}/{Key}", tenantId, bucketName, key);
            throw new InvalidOperationException($"Failed to write file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Opens a span for a document operation: only identity/metadata tags are attached (tenant, bucket, key), never content.
    /// Spans never leave this adapter to prevent cross-tenant data leaks.
    /// </summary>
    private static Activity? StartStorageActivity(string operationName, Guid tenantId, string bucketName, string key)
    {
        var activity = SenseActivitySources.Documents.StartActivity(operationName, ActivityKind.Client);
        activity?.SetTag("sense.tenant.id", tenantId);
        activity?.SetTag("sense.storage.bucket", bucketName);
        activity?.SetTag("sense.storage.key", key);
        return activity;
    }

    /// <summary>Creates a tenant-isolated bucket name.</summary>
    private static string CreateBucketName(Guid tenantId, string bucketName)
    {
        // MinIO bucket names must be lowercase, 3-63 chars, start with letter/digit, contain only alphanumeric/dash
        var tenant = tenantId.ToString(RequestIdFormat).ToLowerInvariant()[..TenantPrefixLength]; // Use first 12 chars of tenant ID
        var name = bucketName.ToLowerInvariant().Replace('_', '-');
        return $"{tenant}-{name}";
    }

    /// <summary>
    /// Best-effort object-not-found detection. MinIO surfaces S3 errors with codes like "NoSuchKey" inside the
    /// exception message, so match on the known markers until the SDK exposes a typed error. Case-insensitive,
    /// ordinal comparison to avoid culture-dependent behaviour.
    /// </summary>
    private static bool IsObjectNotFound(Exception ex) =>
        ex.Message.Contains("NoSuchKey", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates input parameters for security and correctness.</summary>
    private static void ValidateParameters(Guid tenantId, string bucketName, string key)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant ID cannot be empty", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new ArgumentException("Bucket name cannot be empty", nameof(bucketName));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be empty", nameof(key));
        }

        // Ensure key doesn't try to escape the tenant bucket
        if (key.Contains("..", StringComparison.Ordinal) || key.StartsWith('/'))
        {
            throw new ArgumentException("Invalid key format", nameof(key));
        }
    }
}
