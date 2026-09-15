using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Notifications.Domain.Preferences;

/// <summary>
/// A user's explicit on/off choice for one template on one channel. Absent row = the catalog's default for that
/// channel; mandatory templates ignore it. (UserId, TemplateKey, Channel) is unique.
/// </summary>
public sealed class NotificationPreference : TenantEntity<Guid>
{
    private NotificationPreference()
    {
    }

    public NotificationPreference(Guid tenantId, Guid userId, string templateKey, string channel, bool enabled)
        : base(Guid.CreateVersion7(), tenantId)
    {
        UserId = userId;
        TemplateKey = Guard.MaxLength(templateKey, NotificationsLimits.TemplateKeyMaxLength);
        Channel = Guard.MaxLength(channel, NotificationsLimits.ChannelMaxLength);
        Enabled = enabled;
    }

    public Guid UserId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public string Channel { get; private set; } = string.Empty;

    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled) => Enabled = enabled;
}
