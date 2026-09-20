using Sense.Modules.Documents.Contracts;
using Sense.Modules.Leave.Domain;
using Sense.Modules.Leave.Domain.Requests;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Application.Requests;

/// <summary>Denetim A5-belge: talep detayında gösterilen ek (dosya adı anlık görüntüsü).</summary>
public sealed record LeaveRequestAttachmentDto(Guid DocumentId, string FileName, DateTime AttachedAt);

/// <summary>Belge kimliklerini ek satırlarına çevirir (Active doğrulaması + dosya adı anlık görüntüsü). Submit/OnBehalf/Attach paylaşır.</summary>
internal static class LeaveRequestAttachmentLinker
{
    public static async Task<Result> LinkAsync(
        IDocumentAttachmentLookup documents,
        ILeaveRequestAttachmentRepository attachments,
        Guid tenantId,
        Guid leaveRequestId,
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
            if (!active.TryGetValue(documentId, out var info))
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
            documents, attachments, tenant.TenantId, request.Id, [command.DocumentId], currentUser.UserId,
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
