using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Documents.Application.VisaDocuments;

internal static class VisaDocumentRequestMapper
{
    public static VisaDocumentRequestDto ToDto(VisaDocumentRequest r) => new(
        r.Id,
        r.EmployeeId,
        r.VisaDocumentProcessId,
        r.TravelStart,
        r.TravelEnd,
        r.CountryCode,
        r.VisaType,
        r.Description,
        r.Status,
        r.ApprovalRequestId,
        r.RejectionReason,
        r.CreatedAt,
        r.ApprovedAt,
        r.CompletedAt);

    /// <summary>The caller's own request, or null. Someone else's request is indistinguishable from a missing one
    /// (NotFound, not Forbidden) so ids can't be probed.</summary>
    public static async Task<VisaDocumentRequest?> FindOwnAsync(
        IVisaDocumentRequestRepository requests, ICurrentUser currentUser, Guid requestId, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return null;
        }

        var request = await requests.GetByIdAsync(requestId, cancellationToken);
        return request is not null && request.EmployeeId == employeeId ? request : null;
    }
}

/// <summary>Fetches one of the caller's own visa document requests.</summary>
[RequiresPermission("documents.my.visa_requests")]
public sealed record GetVisaDocumentRequestQuery(Guid RequestId) : IQuery<VisaDocumentRequestDto>;

public sealed class GetVisaDocumentRequestHandler(IVisaDocumentRequestRepository requests, ICurrentUser currentUser)
    : IQueryHandler<GetVisaDocumentRequestQuery, VisaDocumentRequestDto>
{
    public async Task<Result<VisaDocumentRequestDto>> Handle(GetVisaDocumentRequestQuery query, CancellationToken cancellationToken) =>
        await VisaDocumentRequestMapper.FindOwnAsync(requests, currentUser, query.RequestId, cancellationToken) is { } request
            ? VisaDocumentRequestMapper.ToDto(request)
            : Error.NotFound(VisaDomainErrors.VisaDocumentRequestNotFound);
}

/// <summary>Lists the caller's own visa document requests, newest first, optionally filtered by status.</summary>
[RequiresPermission("documents.my.visa_requests")]
public sealed record ListMyVisaDocumentRequestsQuery(VisaDocumentRequestStatus? Status) : IQuery<IReadOnlyList<VisaDocumentRequestDto>>;

public sealed class ListMyVisaDocumentRequestsHandler(IVisaDocumentRequestRepository requests, ITenantContext tenant, ICurrentUser currentUser)
    : IQueryHandler<ListMyVisaDocumentRequestsQuery, IReadOnlyList<VisaDocumentRequestDto>>
{
    public async Task<Result<IReadOnlyList<VisaDocumentRequestDto>>> Handle(
        ListMyVisaDocumentRequestsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(VisaDomainErrors.VisaDocumentRequestNoEmployeeForUser);
        }

        var items = await requests.GetByEmployeeIdAsync(tenant.TenantId, employeeId, query.Status, cancellationToken);
        return Result.Success<IReadOnlyList<VisaDocumentRequestDto>>(items.Select(VisaDocumentRequestMapper.ToDto).ToList());
    }
}

/// <summary>Documents delivered into the caller's folder for one of their approved requests, each with a short-lived
/// presigned download URL. Deliberately gated by ownership + <c>documents.my.visa_requests</c> rather than
/// <c>documents.files.read</c>: an employee must see what was delivered to them even where tenant file visibility for
/// employees is off (handoff GATE 1 assumption 3).</summary>
[RequiresPermission("documents.my.visa_requests")]
public sealed record ListVisaRequestDocumentsQuery(Guid RequestId) : IQuery<IReadOnlyList<VisaDeliveredDocumentDto>>;

public sealed class ListVisaRequestDocumentsHandler(
    IVisaDocumentRequestRepository requests,
    IDocumentRepository documents,
    IFileStorage fileStorage,
    ITenantContext tenant,
    ICurrentUser currentUser) : IQueryHandler<ListVisaRequestDocumentsQuery, IReadOnlyList<VisaDeliveredDocumentDto>>
{
    private const int DownloadUrlMinutes = 15;

    public async Task<Result<IReadOnlyList<VisaDeliveredDocumentDto>>> Handle(
        ListVisaRequestDocumentsQuery query, CancellationToken cancellationToken)
    {
        if (await VisaDocumentRequestMapper.FindOwnAsync(requests, currentUser, query.RequestId, cancellationToken) is null)
        {
            return Error.NotFound(VisaDomainErrors.VisaDocumentRequestNotFound);
        }

        var delivered = await documents.ListByVisaRequestAsync(query.RequestId, cancellationToken);
        var items = new List<VisaDeliveredDocumentDto>(delivered.Count);
        foreach (var doc in delivered)
        {
            var url = await fileStorage.GetDownloadUrlAsync(tenant.TenantId, doc.BucketName, doc.FileKey, DownloadUrlMinutes, cancellationToken);
            items.Add(new VisaDeliveredDocumentDto(doc.Id, doc.Title, doc.ContentType, doc.Size, doc.UploadedAt, url.DownloadUrl, url.ExpiresAt));
        }

        return items;
    }
}
