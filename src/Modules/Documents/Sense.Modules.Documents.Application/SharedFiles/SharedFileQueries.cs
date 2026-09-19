using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.SharedFiles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.SharedFiles;

/// <summary>
/// Lists shared company files visible to the caller (paginated). Everyone with the view permission sees
/// tenant-wide files; role-restricted files only show up for callers holding one of the allowed roles.
/// Visibility filtering happens here (not in SQL) because it depends on the caller's role set from
/// <see cref="ICurrentUser"/>, not a column the repository can filter on alone - acceptable at senseik-028's
/// scale (a small shared-file library bounded by the tenant's storage quota, not a paginated document archive).
/// </summary>
[RequiresPermission("documents.company.view")]
public sealed record ListSharedFilesQuery(PagedQuery Paging) : IQuery<PagedResult<SharedFileListItemDto>>;

public sealed class ListSharedFilesHandler(
    ISharedCompanyFileRepository files,
    ICurrentUser currentUser) : IQueryHandler<ListSharedFilesQuery, PagedResult<SharedFileListItemDto>>
{
    /// <summary>Generous upper bound for the "fetch everything, then filter by visibility" read - see the type doc.</summary>
    private const int MaxCandidateRows = 2000;

    public async Task<Result<PagedResult<SharedFileListItemDto>>> Handle(ListSharedFilesQuery query, CancellationToken cancellationToken)
    {
        var all = await files.ListByTenantAsync(0, MaxCandidateRows, cancellationToken);
        var visible = all.Where(f => f.IsVisibleTo(currentUser.Roles)).ToList();

        var skip = (query.Paging.Page - 1) * query.Paging.PageSize;
        var page = visible.Skip(skip).Take(query.Paging.PageSize)
            .Select(ToListItemDto)
            .ToList();

        return new PagedResult<SharedFileListItemDto>(page, query.Paging.Page, query.Paging.PageSize, visible.Count);
    }

    private static SharedFileListItemDto ToListItemDto(SharedCompanyFile f) => new(
        f.Id, f.FileName, f.ContentType, f.Size, f.UploadedAt, f.UploadedBy, f.Visibility.ToString(), f.AllowedRoles);
}

/// <summary>Gets a single shared company file with a presigned download URL. Returns NotFound when the file is
/// role-restricted and the caller does not hold any of the allowed roles (indistinguishable from a missing file,
/// same information-hiding choice as other tenant-scoped 404s in this codebase).</summary>
[RequiresPermission("documents.company.view")]
public sealed record GetSharedFileQuery(Guid FileId) : IQuery<SharedFileDetailDto>;

public sealed class GetSharedFileHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser,
    ISharedCompanyFileRepository files) : IQueryHandler<GetSharedFileQuery, SharedFileDetailDto>
{
    public async Task<Result<SharedFileDetailDto>> Handle(GetSharedFileQuery query, CancellationToken cancellationToken)
    {
        var file = await files.GetByIdAsync(query.FileId, cancellationToken);
        if (file.IsFailure)
        {
            return file.Error;
        }

        if (!file.Value.IsVisibleTo(currentUser.Roles))
        {
            return Error.NotFound(SharedFilesDomainErrors.NotFound);
        }

        var downloadUrl = await fileStorage.GetDownloadUrlAsync(
            tenant.TenantId,
            SharedCompanyFile.BucketName,
            file.Value.FileKey,
            expirationMinutes: 15,
            cancellationToken);

        return new SharedFileDetailDto(
            file.Value.Id,
            file.Value.FileName,
            file.Value.ContentType,
            file.Value.Size,
            file.Value.UploadedAt,
            downloadUrl.DownloadUrl,
            downloadUrl.ExpiresAt);
    }
}

/// <summary>Tenant's shared-file storage quota usage (senseik-028 acceptance: usage indicator alongside the file list).</summary>
[RequiresPermission("documents.company.view")]
public sealed record GetSharedFileQuotaQuery : IQuery<SharedFileQuotaDto>;

public sealed class GetSharedFileQuotaHandler(ISharedCompanyFileRepository files) : IQueryHandler<GetSharedFileQuotaQuery, SharedFileQuotaDto>
{
    public async Task<Result<SharedFileQuotaDto>> Handle(GetSharedFileQuotaQuery query, CancellationToken cancellationToken)
    {
        var usedBytes = await files.SumActiveSizeByTenantAsync(cancellationToken);
        var quota = SharedFileQuota.Calculate(usedBytes, SharedFilesLimits.DefaultQuotaBytes);
        return new SharedFileQuotaDto(quota.UsedBytes, quota.QuotaBytes, quota.UsedPercent, quota.IsExceeded);
    }
}
