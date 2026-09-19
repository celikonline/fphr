using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
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
}

/// <summary>Fetches a single visa document request by id, tenant-scoped by the repository.</summary>
[RequiresPermission("documents.visa.request")]
public sealed record GetVisaDocumentRequestQuery(Guid RequestId) : IQuery<VisaDocumentRequestDto>;

public sealed class GetVisaDocumentRequestHandler(IVisaDocumentRequestRepository requests)
    : IQueryHandler<GetVisaDocumentRequestQuery, VisaDocumentRequestDto>
{
    public async Task<Result<VisaDocumentRequestDto>> Handle(GetVisaDocumentRequestQuery query, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(query.RequestId, cancellationToken);
        return request is null
            ? Error.NotFound(VisaDomainErrors.VisaDocumentRequestNotFound)
            : VisaDocumentRequestMapper.ToDto(request);
    }
}

/// <summary>Lists an employee's visa document requests, optionally filtered by status.</summary>
[RequiresPermission("documents.visa.request")]
public sealed record ListVisaDocumentRequestsQuery(Guid EmployeeId, VisaDocumentRequestStatus? Status) : IQuery<IReadOnlyList<VisaDocumentRequestDto>>;

public sealed class ListVisaDocumentRequestsHandler(IVisaDocumentRequestRepository requests, ITenantContext tenant)
    : IQueryHandler<ListVisaDocumentRequestsQuery, IReadOnlyList<VisaDocumentRequestDto>>
{
    public async Task<Result<IReadOnlyList<VisaDocumentRequestDto>>> Handle(
        ListVisaDocumentRequestsQuery query, CancellationToken cancellationToken)
    {
        var items = await requests.GetByEmployeeIdAsync(tenant.TenantId, query.EmployeeId, query.Status, cancellationToken);
        return Result.Success<IReadOnlyList<VisaDocumentRequestDto>>(
            items.Select(VisaDocumentRequestMapper.ToDto).ToList());
    }
}

/// <summary>Read model for a visa document process (the employee-facing selection list).</summary>
public sealed record VisaDocumentProcessDto(Guid Id, string Name, string? CountryCode, bool IsActive);

/// <summary>Lists the tenant's active visa document processes (the employee-facing selection list per the
/// handoff's API layer task; <c>GET /api/v1/visa-processes?isActive=true</c> - there is no repository support yet
/// for listing inactive ones too, that's an admin/settings-page concern deferred with process CRUD).</summary>
[RequiresPermission("documents.visa.request")]
public sealed record ListVisaDocumentProcessesQuery : IQuery<IReadOnlyList<VisaDocumentProcessDto>>;

public sealed class ListVisaDocumentProcessesHandler(IVisaDocumentProcessRepository processes, ITenantContext tenant)
    : IQueryHandler<ListVisaDocumentProcessesQuery, IReadOnlyList<VisaDocumentProcessDto>>
{
    public async Task<Result<IReadOnlyList<VisaDocumentProcessDto>>> Handle(
        ListVisaDocumentProcessesQuery query, CancellationToken cancellationToken)
    {
        var items = await processes.GetActiveAsync(tenant.TenantId, cancellationToken);
        return Result.Success<IReadOnlyList<VisaDocumentProcessDto>>(
            items.Select(p => new VisaDocumentProcessDto(p.Id, p.Name, p.CountryCode, p.IsActive)).ToList());
    }
}
