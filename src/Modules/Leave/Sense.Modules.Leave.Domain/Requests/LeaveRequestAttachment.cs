using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Leave.Domain.Requests;

/// <summary>
/// Denetim raporu A5-belge: talebe eklenen belge (rapor, doğum belgesi...). Dosyanın kendisi Documents modülünde
/// (<see cref="DocumentId"/>), burada yalnız bağ + dosya adı anlık görüntüsü tutulur (talep detayı/denetim için;
/// yeniden adlandırma geçmişi bozmaz). Ekleme/çıkarma yalnız Pending taleplerde, komut katmanında denetlenir.
/// </summary>
public sealed class LeaveRequestAttachment : TenantAggregateRoot<Guid>
{
    private LeaveRequestAttachment()
    {
    }

    private LeaveRequestAttachment(Guid tenantId, Guid leaveRequestId, Guid documentId, string fileName, Guid? attachedByUserId, DateTime attachedAt)
        : base(Guid.CreateVersion7(), tenantId)
    {
        LeaveRequestId = leaveRequestId;
        DocumentId = documentId;
        FileName = fileName;
        AttachedByUserId = attachedByUserId;
        AttachedAt = attachedAt;
    }

    public Guid LeaveRequestId { get; private set; }

    public Guid DocumentId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    /// <summary>Null for system entries (API key / anomaly flows without a user).</summary>
    public Guid? AttachedByUserId { get; private set; }

    public DateTime AttachedAt { get; private set; }

    public static Result<LeaveRequestAttachment> Attach(
        Guid tenantId, Guid leaveRequestId, Guid documentId, string fileName, Guid? attachedByUserId, DateTime attachedAt)
    {
        if (leaveRequestId == Guid.Empty || documentId == Guid.Empty)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidAttachment);
        }

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Trim().Length > LeaveLimits.AttachmentFileNameMaxLength)
        {
            return Error.Validation(LeaveDomainErrors.RequestInvalidAttachment);
        }

        return new LeaveRequestAttachment(tenantId, leaveRequestId, documentId, fileName.Trim(), attachedByUserId, attachedAt);
    }
}
