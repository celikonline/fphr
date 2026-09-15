using Sense.Modules.Notifications.Contracts;

namespace Sense.Modules.Notifications.Application.Catalog;

public enum NotificationCategory
{
    Account,
    Request,
    Approval,
}

/// <summary>
/// One template in the catalog (11.3.13). <see cref="DefaultChannels"/> decide delivery when the user has no
/// preference row; <see cref="IsMandatory"/> templates are always delivered on their default channels and cannot be
/// turned off.
/// </summary>
public sealed record NotificationTemplateDefinition(
    string Key, NotificationCategory Category, IReadOnlyList<string> DefaultChannels, bool IsMandatory)
{
    public bool IsUserConfigurable => !IsMandatory;

    public bool SupportsChannel(string channel) => DefaultChannels.Contains(channel, StringComparer.Ordinal);
}

/// <summary>
/// Templates this module delivers today. Other modules' keys (leave.*, shift.*, identity.* ...) join here as their
/// events are consumed; identity invitation/password reset mails are still sent by Identity itself.
/// </summary>
public static class NotificationCatalog
{
    private static readonly string[] EmailAndInApp = [NotificationChannels.Email, NotificationChannels.InApp];

    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } =
    [
        new(NotificationTemplateKeys.RequestCreated, NotificationCategory.Request, EmailAndInApp, IsMandatory: false),
        new(NotificationTemplateKeys.RequestApproved, NotificationCategory.Request, EmailAndInApp, IsMandatory: false),
        new(NotificationTemplateKeys.RequestRejected, NotificationCategory.Request, EmailAndInApp, IsMandatory: false),
        new(NotificationTemplateKeys.ApprovalTaskAssigned, NotificationCategory.Approval, EmailAndInApp, IsMandatory: false),
    ];

    public static NotificationTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>Effective on/off for a user: mandatory → on; explicit preference wins; otherwise the channel default.</summary>
    public static bool IsEnabled(NotificationTemplateDefinition definition, string channel, bool? preference)
    {
        if (!definition.SupportsChannel(channel))
        {
            return false;
        }

        return definition.IsMandatory || (preference ?? true);
    }
}
