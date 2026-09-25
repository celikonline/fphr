using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Notifications.Domain.HrMessages;

public enum HrMessageImportance
{
    Normal = 0,
    High = 1,
}

/// <summary>
/// A free-form message HR sent to employees. The recipient list is a snapshot taken at send time (group/filter
/// selections are resolved once); inbox/outbox are derived from <see cref="SenderUserId"/> and the recipients' user ids.
/// </summary>
public sealed class HrMessage : TenantAggregateRoot<Guid>
{
    private readonly List<HrMessageRecipient> _recipients = [];

    private HrMessage()
    {
    }

    public HrMessage(
        Guid tenantId, Guid senderUserId, string subject, string body, HrMessageImportance importance, IReadOnlyCollection<string> channels)
        : base(Guid.CreateVersion7(), tenantId)
    {
        Guard.NotEmpty(subject);
        Guard.NotEmpty(body);
        Guard.Against(channels.Count == 0, "At least one channel is required.");
        SenderUserId = senderUserId;
        Subject = Guard.MaxLength(subject.Trim(), NotificationsLimits.HrMessageSubjectMaxLength);
        Body = Guard.MaxLength(body, NotificationsLimits.HrMessageBodyMaxLength);
        Importance = importance;
        Channels = Guard.MaxLength(string.Join(',', channels), NotificationsLimits.HrMessageChannelsMaxLength);
    }

    public Guid SenderUserId { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public HrMessageImportance Importance { get; private set; }

    /// <summary>Comma-separated channel names ("email,in_app").</summary>
    public string Channels { get; private set; } = string.Empty;

    public bool IsHighPriority => Importance == HrMessageImportance.High;

    public IReadOnlyCollection<HrMessageRecipient> Recipients => _recipients;

    public void AddRecipient(Guid employeeId, Guid? userId) =>
        _recipients.Add(new HrMessageRecipient(TenantId, Id, employeeId, userId));
}

/// <summary>One resolved recipient. <see cref="UserId"/> is null for employees without an active account (e-mail only).</summary>
public sealed class HrMessageRecipient : TenantEntity<Guid>
{
    private HrMessageRecipient()
    {
    }

    internal HrMessageRecipient(Guid tenantId, Guid messageId, Guid employeeId, Guid? userId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        MessageId = messageId;
        EmployeeId = employeeId;
        UserId = userId;
    }

    public Guid MessageId { get; private set; }

    public Guid EmployeeId { get; private set; }

    public Guid? UserId { get; private set; }
}
