using Sense.Modules.Documents.Contracts;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.Documents;

/// <summary>Gets a single document with presigned download URL.</summary>
[RequiresPermission("documents.files.read")]
public sealed record GetDocumentQuery(Guid DocumentId) : IQuery<DocumentDetailDto>;

public sealed class GetDocumentHandler(
    IFileStorage fileStorage,
    ITenantContext tenant,
    IDocumentRepository documents) : IQueryHandler<GetDocumentQuery, DocumentDetailDto>
{
    public async Task<Result<DocumentDetailDto>> Handle(GetDocumentQuery query, CancellationToken cancellationToken)
    {
        var doc = await documents.GetByIdAsync(query.DocumentId, cancellationToken);
        if (doc.IsFailure)
        {
            return doc.Error;
        }

        // Generate presigned download URL (valid 15 minutes)
        var downloadUrl = await fileStorage.GetDownloadUrlAsync(
            tenant.TenantId,
            doc.Value.BucketName,
            doc.Value.FileKey,
            expirationMinutes: 15,
            cancellationToken);

        return new DocumentDetailDto(
            doc.Value.Id,
            doc.Value.Title,
            doc.Value.ContentType,
            doc.Value.Size,
            doc.Value.UploadedAt,
            doc.Value.Status.ToString(),
            downloadUrl.DownloadUrl,
            downloadUrl.ExpiresAt);
    }
}

/// <summary>Lists all documents for the current tenant (paginated).</summary>
[RequiresPermission("documents.files.read")]
public sealed record ListDocumentsQuery(PagedQuery Paging) : IQuery<PagedResult<DocumentListItemDto>>;

public sealed class ListDocumentsHandler(
    IDocumentRepository documents) : IQueryHandler<ListDocumentsQuery, PagedResult<DocumentListItemDto>>
{
    public async Task<Result<PagedResult<DocumentListItemDto>>> Handle(ListDocumentsQuery query, CancellationToken cancellationToken)
    {
        var total = await documents.CountByTenantAsync(cancellationToken);

        var skip = (query.Paging.Page - 1) * query.Paging.PageSize;
        var docs = await documents.ListByTenantAsync(skip, query.Paging.PageSize, cancellationToken);

        var items = docs
            .Select(d => new DocumentListItemDto(
                d.Id,
                d.Title,
                d.ContentType,
                d.Size,
                d.UploadedAt,
                d.Status.ToString()))
            .ToList();

        return new PagedResult<DocumentListItemDto>(
            items,
            query.Paging.Page,
            query.Paging.PageSize,
            total);
    }
}
