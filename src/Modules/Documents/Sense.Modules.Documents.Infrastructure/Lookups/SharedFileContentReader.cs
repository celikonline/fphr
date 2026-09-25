using Sense.Modules.Documents.Application.SharedFiles;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Storage;

namespace Sense.Modules.Documents.Infrastructure.Lookups;

/// <summary>Reads shared-file bytes through a short-lived presigned download URL (IFileStorage has no direct read).</summary>
public sealed class SharedFileContentReader(IFileStorage storage, IHttpClientFactory httpClientFactory) : ISharedFileContentReader
{
    public const string HttpClientName = "documents-shared-file-reader";
    private const int UrlMinutes = 5;

    public async Task<byte[]?> ReadAsync(Guid tenantId, string fileKey, CancellationToken cancellationToken = default)
    {
        var url = await storage.GetDownloadUrlAsync(tenantId, SharedCompanyFile.BucketName, fileKey, UrlMinutes, cancellationToken);
        using var response = await httpClientFactory.CreateClient(HttpClientName).GetAsync(url.DownloadUrl, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
