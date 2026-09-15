using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Notifications.Domain.Deliveries;

public enum NotificationDeliveryStatus
{
    Pending,
    Sent,
    Failed,
    Skipped,
}

/// <summary>
/// One outgoing message (today: e-mail) - both the send queue and the delivery log. Created Pending by the event
/// handlers with the template variables (<see cref="ModelJson"/>) and recipient culture; rendered and sent later by
/// the Worker's delivery processor so SMTP failures retry here instead of re-running every handler of the source
/// integration event. <see cref="SourceEventId"/> + <see cref="TemplateKey"/> + <see cref="Channel"/> +
/// <see cref="RecipientAddress"/> is unique (idempotent under event re-delivery).
/// </summary>
public sealed class NotificationDelivery : TenantEntity<Guid>
{
    private NotificationDelivery()
    {
    }

    public NotificationDelivery(
        Guid tenantId,
        Guid sourceEventId,
        string templateKey,
        string channel,
        Guid? recipientEmployeeId,
        Guid? recipientUserId,
        string recipientAddress,
        string recipientName,
        string addressSource,
        string culture,
        string modelJson,
        DateTime now)
        : base(Guid.CreateVersion7(), tenantId)
    {
        SourceEventId = sourceEventId;
        TemplateKey = Guard.MaxLength(templateKey, NotificationsLimits.TemplateKeyMaxLength);
        Channel = Guard.MaxLength(channel, NotificationsLimits.ChannelMaxLength);
        RecipientEmployeeId = recipientEmployeeId;
        RecipientUserId = recipientUserId;
        RecipientAddress = Guard.MaxLength(Guard.NotEmpty(recipientAddress), NotificationsLimits.AddressMaxLength);
        RecipientName = recipientName.Length <= NotificationsLimits.RecipientNameMaxLength ? recipientName : recipientName[..NotificationsLimits.RecipientNameMaxLength];
        AddressSource = Guard.MaxLength(addressSource, NotificationsLimits.AddressSourceMaxLength);
        Culture = Guard.MaxLength(culture, NotificationsLimits.CultureMaxLength);
        ModelJson = modelJson;
        Status = NotificationDeliveryStatus.Pending;
        NextAttemptAt = now;
    }

    public Guid SourceEventId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public string Channel { get; private set; } = string.Empty;

    public Guid? RecipientEmployeeId { get; private set; }

    public Guid? RecipientUserId { get; private set; }

    public string RecipientAddress { get; private set; } = string.Empty;

    public string RecipientName { get; private set; } = string.Empty;

    /// <summary>Where the address came from: work_email / personal_email / user_email (11.3.13 "AddressSource").</summary>
    public string AddressSource { get; private set; } = string.Empty;

    /// <summary>Two-letter language the message is rendered in (tr/en/de).</summary>
    public string Culture { get; private set; } = string.Empty;

    /// <summary>Template variables as a JSON object of strings.</summary>
    public string ModelJson { get; private set; } = "{}";

    public NotificationDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    /// <summary>Last send error, or the reason a delivery was skipped.</summary>
    public string? LastError { get; private set; }

    public void MarkSent(DateTime now)
    {
        Attempts++;
        Status = NotificationDeliveryStatus.Sent;
        SentAt = now;
        NextAttemptAt = null;
        LastError = null;
    }

    public void MarkSkipped(string reason)
    {
        Status = NotificationDeliveryStatus.Skipped;
        NextAttemptAt = null;
        LastError = Clip(reason);
    }

    /// <summary>Exponential backoff (2^attempts x base, capped); Failed for good once <paramref name="maxAttempts"/> is reached.</summary>
    public void RecordFailure(string error, DateTime now, int maxAttempts, int baseBackoffSeconds, int maxBackoffSeconds)
    {
        Attempts++;
        LastError = Clip(error);
        if (Attempts >= maxAttempts)
        {
            Status = NotificationDeliveryStatus.Failed;
            NextAttemptAt = null;
            return;
        }

        var seconds = Math.Min(Math.Pow(2, Attempts) * baseBackoffSeconds, maxBackoffSeconds);
        NextAttemptAt = now.AddSeconds(seconds);
    }

    private static string Clip(string value) =>
        value.Length <= NotificationsLimits.ErrorMaxLength ? value : value[..NotificationsLimits.ErrorMaxLength];
}
