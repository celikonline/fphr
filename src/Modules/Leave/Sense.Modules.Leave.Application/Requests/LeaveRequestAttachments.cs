using Sense.Modules.Documents.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

/// <summary>Denetim A5-belge: talep detayında gösterilen ek (dosya adı anlık görüntüsü).</summary>
public sealed record LeaveRequestAttachmentDto(Guid DocumentId, string FileName, DateTime AttachedAt);

/// <summary>Belge kimliklerini ek satırlarına çevirir (Active doğrulaması + dosya adı anlık görüntüsü + sahiplik).
/// Submit/OnBehalf/Attach paylaşır.</summary>
internal static class LeaveRequestAttachmentLinker
{
    /// <summary>
    /// IDOR guard: a document may only be attached when the caller owns it, one way or another. Self-service
    /// (<paramref name="ownerEmployeeId"/> is the caller's own employee) and on-behalf (it's the target employee)
    /// collapse to the same rule: the document must have been uploaded by the acting user, OR it must belong
    /// (<see cref="AttachedDocumentInfo.EmployeeId"/>, e.g. a system-delivered copy) to the request's employee.
    /// Without this, any Active document id - including another employee's medical report - could be attached.
    /// </summary>
    private static bool IsOwnedByCaller(AttachedDocumentInfo info, Guid ownerEmployeeId, Guid? actingUserId) =>
        info.EmployeeId == ownerEmployeeId || (actingUserId is { } userId && info.UploadedBy == userId);

    public static async Task<Result> LinkAsync(
        IDocumentAttachmentLookup documents,
        ILeaveRequestAttachmentRepository attachments,
        Guid tenantId,
        Guid leaveRequestId,
        Guid ownerEmployeeId,
        IReadOnlyCollection<Guid> documentIds,
        Guid? attachedByUserId,
        DateTime attachedAt,
        CancellationToken ct)
    {
        if (documentIds.Count == 0)
        {
            return Result.Success();
        }

        var active = await documents.GetActiveAsync(tenantId, documentIds, ct);
        foreach (var documentId in documentIds)
        {
            if (!active.TryGetValue(documentId, out var info) || !IsOwnedByCaller(info, ownerEmployeeId, attachedByUserId))
            {
                return Error.Validation(LeaveRequestErrors.DocumentNotFound);
            }

            var attached = LeaveRequestAttachment.Attach(tenantId, leaveRequestId, documentId, info.FileName, attachedByUserId, attachedAt);
            if (attached.IsFailure)
            {
                return attached.Error;
            }

            attachments.Add(attached.Value);
        }

        return Result.Success();
    }
}

/// <summary>Self-servis: bekleyen talebe belge ekleme (talep sahibi). Yinelenen belge yoksayılır (idempotent).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record AttachMyLeaveDocumentCommand(Guid RequestId, Guid DocumentId) : ICommand;

public sealed class AttachMyLeaveDocumentHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILeaveRequestRepository requests, ILeaveRequestAttachmentRepository attachments,
    IDocumentAttachmentLookup documents, TimeProvider clock)
    : ICommandHandler<AttachMyLeaveDocumentCommand>
{
    public async Task<Result> Handle(AttachMyLeaveDocumentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null || request.EmployeeId != employeeId)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        if (request.Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        var existing = await attachments.ListByRequestAsync(request.Id, cancellationToken);
        if (existing.Any(a => a.DocumentId == command.DocumentId))
        {
            return Result.Success();
        }

        return await LeaveRequestAttachmentLinker.LinkAsync(
            documents, attachments, tenant.TenantId, request.Id, employeeId, [command.DocumentId], currentUser.UserId,
            clock.GetUtcNow().UtcDateTime, cancellationToken);
    }
}

/// <summary>Self-servis: bekleyen talepten belge çıkarma (talep sahibi).</summary>
[RequiresPermission("leave.my.requests")]
public sealed record DetachMyLeaveDocumentCommand(Guid RequestId, Guid DocumentId) : ICommand;

public sealed class DetachMyLeaveDocumentHandler(
    ICurrentUser currentUser, ILeaveRequestRepository requests, ILeaveRequestAttachmentRepository attachments)
    : ICommandHandler<DetachMyLeaveDocumentCommand>
{
    public async Task<Result> Handle(DetachMyLeaveDocumentCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var request = await requests.FindByIdAsync(command.RequestId, cancellationToken);
        if (request is null || request.EmployeeId != employeeId)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        if (request.Status != LeaveRequestStatus.Pending)
        {
            return Error.Conflict(LeaveDomainErrors.RequestNotPending);
        }

        var existing = await attachments.ListByRequestAsync(request.Id, cancellationToken);
        var row = existing.FirstOrDefault(a => a.DocumentId == command.DocumentId);
        if (row is null)
        {
            return Error.NotFound(LeaveRequestErrors.AttachmentNotFound);
        }

        attachments.Remove(row);
        return Result.Success();
    }
}

/// <summary>Kısa ömürlü ek indirme bağlantısı: URL, geçerlilik süresi, dosya adı/tipi.</summary>
public sealed record LeaveRequestAttachmentDownloadUrlDto(string Url, DateTime ExpiresAt, string FileName, string ContentType);

/// <summary>
/// Attach/AttachMy paylaşır: belge gerçekten bu talebe ekli mi (yoksa <see cref="LeaveRequestErrors.AttachmentNotFound"/>),
/// sonra Documents'tan presigned URL istenir (Active değilse/silinmişse <see cref="LeaveRequestErrors.DocumentNotFound"/>
/// - varlığı sızdırmadan, aynı "bulunamadı" ailesi).
/// </summary>
internal static class LeaveRequestAttachmentDownloadUrlResolver
{
    public static async Task<Result<LeaveRequestAttachmentDownloadUrlDto>> ResolveAsync(
        IDocumentAttachmentLookup documents, ILeaveRequestAttachmentRepository attachments, Guid tenantId, Guid requestId, Guid documentId, CancellationToken ct)
    {
        var attached = await attachments.ListByRequestAsync(requestId, ct);
        if (attached.All(a => a.DocumentId != documentId))
        {
            return Error.NotFound(LeaveRequestErrors.AttachmentNotFound);
        }

        var url = await documents.GetDownloadUrlAsync(tenantId, documentId, ct);
        return url is null
            ? Error.NotFound(LeaveRequestErrors.DocumentNotFound)
            : new LeaveRequestAttachmentDownloadUrlDto(url.Url, url.ExpiresAt, url.FileName, url.ContentType);
    }
}

/// <summary>Onaycı/İK: talebe ekli belgenin indirme bağlantısı. Detay ucuyla (<see cref="GetEmployeeLeaveRequestQuery"/>)
/// aynı yetki kullanılır - ad zaten o yetkiyle görünüyor, ek indirme daha sıkı bir kural gerektirmez.</summary>
[RequiresPermission("leave.requests.read")]
public sealed record GetEmployeeLeaveRequestAttachmentDownloadUrlQuery(Guid EmployeeId, Guid RequestId, Guid DocumentId)
    : IQuery<LeaveRequestAttachmentDownloadUrlDto>;

public sealed class GetEmployeeLeaveRequestAttachmentDownloadUrlHandler(
    ITenantContext tenant, IDataScopeService dataScope, ILeaveRequestRepository requests, ILeaveRequestAttachmentRepository attachments,
    IDocumentAttachmentLookup documents)
    : IQueryHandler<GetEmployeeLeaveRequestAttachmentDownloadUrlQuery, LeaveRequestAttachmentDownloadUrlDto>
{
    public async Task<Result<LeaveRequestAttachmentDownloadUrlDto>> Handle(GetEmployeeLeaveRequestAttachmentDownloadUrlQuery query, CancellationToken cancellationToken)
    {
        if (!await dataScope.CanAccessEmployeeAsync(LeavePermissions.RequestsRead.Key, query.EmployeeId, cancellationToken))
        {
            return Error.Forbidden(LeaveErrors.EmployeeNotInScope);
        }

        var request = await requests.FindByIdAsync(query.RequestId, cancellationToken);
        if (request is null || request.EmployeeId != query.EmployeeId)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        return await LeaveRequestAttachmentDownloadUrlResolver.ResolveAsync(documents, attachments, tenant.TenantId, request.Id, query.DocumentId, cancellationToken);
    }
}

/// <summary>Self-servis: talep sahibinin kendi ekini indirmesi.</summary>
[RequiresPermission("leave.my.requests")]
public sealed record GetMyLeaveRequestAttachmentDownloadUrlQuery(Guid RequestId, Guid DocumentId) : IQuery<LeaveRequestAttachmentDownloadUrlDto>;

public sealed class GetMyLeaveRequestAttachmentDownloadUrlHandler(
    ITenantContext tenant, ICurrentUser currentUser, ILeaveRequestRepository requests, ILeaveRequestAttachmentRepository attachments,
    IDocumentAttachmentLookup documents)
    : IQueryHandler<GetMyLeaveRequestAttachmentDownloadUrlQuery, LeaveRequestAttachmentDownloadUrlDto>
{
    public async Task<Result<LeaveRequestAttachmentDownloadUrlDto>> Handle(GetMyLeaveRequestAttachmentDownloadUrlQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.EmployeeId is not { } employeeId)
        {
            return Error.NotFound(LeaveErrors.NoEmployeeForUser);
        }

        var request = await requests.FindByIdAsync(query.RequestId, cancellationToken);
        if (request is null || request.EmployeeId != employeeId)
        {
            return Error.NotFound(LeaveDomainErrors.RequestNotFound);
        }

        return await LeaveRequestAttachmentDownloadUrlResolver.ResolveAsync(documents, attachments, tenant.TenantId, request.Id, query.DocumentId, cancellationToken);
    }
}
