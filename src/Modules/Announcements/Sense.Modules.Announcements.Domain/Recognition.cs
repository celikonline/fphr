using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Announcements.Domain;

/// <summary>Bir çalışanın başka bir çalışana gönderdiği kısa takdir/teşekkür mesajı; Announcements akışında görünür.</summary>
public sealed class Recognition : TenantEntity<Guid>
{
    // ReSharper disable once UnusedMember.Local
    private Recognition() { }

    public Guid SenderEmployeeId { get; private set; }
    public Guid ReceiverEmployeeId { get; private set; }
    public string Message { get; private set; } = null!;

    public static Result<Recognition> Create(Guid tenantId, Guid senderEmployeeId, Guid receiverEmployeeId, string message)
    {
        if (senderEmployeeId == receiverEmployeeId)
        {
            return Error.Validation(RecognitionDomainErrors.CannotRecognizeSelf);
        }

        if (string.IsNullOrWhiteSpace(message) || message.Length > AnnouncementsLimits.RecognitionMessageMaxLength)
        {
            return Error.Validation(RecognitionDomainErrors.MessageRequired);
        }

        return new Recognition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SenderEmployeeId = senderEmployeeId,
            ReceiverEmployeeId = receiverEmployeeId,
            Message = message.Trim(),
        };
    }
}
