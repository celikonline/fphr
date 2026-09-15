using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Domain.Preferences;

/// <summary>
/// A user's channel override for one template key. Absence of a row means the catalog default (e-mail + in-app on);
/// rows are only written when the user saves the preferences screen.
/// </summary>
public sealed class NotificationPreference : TenantAggregateRoot<Guid>
{
    private NotificationPreference()
    {
    }

    private NotificationPreference(Guid id, Guid tenantId, Guid userId, string templateKey, bool emailEnabled, bool inAppEnabled)
        : base(id, tenantId)
    {
        UserId = userId;
        TemplateKey = templateKey;
        EmailEnabled = emailEnabled;
        InAppEnabled = inAppEnabled;
    }

    public Guid UserId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public bool EmailEnabled { get; private set; }

    public bool InAppEnabled { get; private set; }

    public static Result<NotificationPreference> Create(Guid tenantId, Guid userId, string templateKey, bool emailEnabled, bool inAppEnabled)
    {
        if (userId == Guid.Empty)
        {
            return Error.Validation(NotificationsDomainErrors.RecipientRequired);
        }

        if (string.IsNullOrWhiteSpace(templateKey) || templateKey.Length > NotificationLimits.TemplateKeyMaxLength)
        {
            return Error.Validation(NotificationsDomainErrors.TemplateKeyInvalid);
        }

        return new NotificationPreference(Guid.CreateVersion7(), tenantId, userId, templateKey, emailEnabled, inAppEnabled);
    }

    public void Update(bool emailEnabled, bool inAppEnabled)
    {
        EmailEnabled = emailEnabled;
        InAppEnabled = inAppEnabled;
    }
}
