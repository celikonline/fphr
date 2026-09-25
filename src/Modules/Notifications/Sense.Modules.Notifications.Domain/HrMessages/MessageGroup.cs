using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Notifications.Domain.HrMessages;

/// <summary>
/// A named, persistent recipient group. Stores the employee selection definition (ids and/or grid filter, JSON) rather
/// than a member list, so membership is resolved when a message is sent.
/// </summary>
public sealed class MessageGroup : TenantAggregateRoot<Guid>
{
    private MessageGroup()
    {
    }

    public MessageGroup(Guid tenantId, string name, string selectionJson)
        : base(Guid.CreateVersion7(), tenantId)
    {
        Name = Guard.MaxLength(Guard.NotEmpty(name).Trim(), NotificationsLimits.MessageGroupNameMaxLength);
        SelectionJson = Guard.NotEmpty(selectionJson);
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Serialized employee selection (ids/filter/excluded ids).</summary>
    public string SelectionJson { get; private set; } = string.Empty;

    public void Update(string name, string selectionJson)
    {
        Name = Guard.MaxLength(Guard.NotEmpty(name).Trim(), NotificationsLimits.MessageGroupNameMaxLength);
        SelectionJson = Guard.NotEmpty(selectionJson);
    }
}
