using System.IO.Compression;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.SharedFiles;

/// <summary>Reads a stored shared file's bytes (IFileStorage exposes only presigned URLs, so the adapter lives in Infrastructure).</summary>
public interface ISharedFileContentReader
{
    /// <summary>The object's bytes, or null when it no longer exists in storage.</summary>
    Task<byte[]?> ReadAsync(Guid tenantId, string fileKey, CancellationToken cancellationToken = default);
}

public sealed record SharedFileZipResult(byte[] Content, string FileName, IReadOnlyList<string> IncludedFiles);

/// <summary>
/// Bulk download (E8) of shared files as one zip. Selection = explicit ids and/or the same mobile/type filters as the
/// list; whatever the selection, a file the caller may not see (role-restricted) or that is not Active is left out.
/// </summary>
[RequiresPermission("documents.company.view")]
public sealed record DownloadSharedFilesZipQuery(
    IReadOnlyList<Guid>? FileIds = null,
    bool? MobileOnly = null,
    Guid? TypeId = null) : IQuery<SharedFileZipResult>;

public sealed class DownloadSharedFilesZipHandler(
    ISharedCompanyFileRepository files,
    ISharedFileContentReader reader,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : IQueryHandler<DownloadSharedFilesZipQuery, SharedFileZipResult>
{
    public const int MaxFiles = 200;
    private const int MaxCandidateRows = 2000;

    public async Task<Result<SharedFileZipResult>> Handle(DownloadSharedFilesZipQuery query, CancellationToken cancellationToken)
    {
        var all = await files.ListByTenantAsync(0, MaxCandidateRows, cancellationToken);
        var selected = all
            .Where(f => f.Status == SharedFileStatus.Active)
            .Where(f => f.IsVisibleTo(currentUser.Roles))
            .Where(f => query.FileIds is null || query.FileIds.Contains(f.Id))
            .Where(f => query.MobileOnly != true || f.VisibleOnMobile)
            .Where(f => query.TypeId is null || f.TypeId == query.TypeId)
            .ToList();

        if (selected.Count == 0)
        {
            return Error.NotFound(SharedFilesDomainErrors.NothingToDownload);
        }

        if (selected.Count > MaxFiles)
        {
            return Error.Validation(SharedFilesDomainErrors.ZipTooManyFiles, ("max", MaxFiles));
        }

        var included = new List<string>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in selected)
            {
                var content = await reader.ReadAsync(tenant.TenantId, file.FileKey, cancellationToken);
                if (content is null)
                {
                    continue;
                }

                var entryName = UniqueName(file.FileName, usedNames);
                var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(content, cancellationToken);
                included.Add(entryName);
            }
        }

        if (included.Count == 0)
        {
            return Error.NotFound(SharedFilesDomainErrors.NothingToDownload);
        }

        var stamp = clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm");
        return new SharedFileZipResult(buffer.ToArray(), $"shared-files-{stamp}.zip", included);
    }

    private static string UniqueName(string fileName, HashSet<string> used)
    {
        var candidate = fileName;
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var n = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{stem} ({n++}){ext}";
        }

        return candidate;
    }
}
