using System.Globalization;
using Sense.Modules.Notifications.Contracts;
using Sense.Modules.Notifications.Domain.Notifications;

namespace Sense.Modules.Notifications.Application;

/// <summary>Application error codes (localized in SharedResource).</summary>
public static class NotificationsErrors
{
    public const string NotificationNotFound = "notifications.not_found";
    public const string UnknownTemplate = "notifications.template_unknown";
    public const string TemplateNotConfigurable = "notifications.template_not_configurable";
}

/// <summary>Rendered text of one template for one culture.</summary>
public sealed record RenderedNotification(string Title, string Body);

/// <summary>Resource-keyed template rendering ("{key}.title"/"{key}.body", {Placeholder} substitution). Implemented by Infrastructure.</summary>
public interface INotificationTemplateRenderer
{
    RenderedNotification Render(string templateKey, IReadOnlyDictionary<string, string> args, CultureInfo culture);
}

/// <summary>Per-user real-time push of a freshly created notification. No-op in hosts without SignalR.</summary>
public interface INotificationRealtimePusher
{
    Task PushAsync(Guid recipientUserId, NotificationItemDto notification, CancellationToken ct);
}

/// <summary>Catalog entry: defaults and whether users may switch channels off.</summary>
public sealed record NotificationTemplateDefinition(
    string Key, NotificationCategory Category, NotificationChannels DefaultChannels, bool IsMandatory);

/// <summary>Code-defined template catalog (11.3.13). Other modules' keys are appended here as they start publishing.</summary>
public static class NotificationTemplateCatalog
{
    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } =
    [
        new(NotificationTemplateKeys.RequestCreated, NotificationCategory.Request, NotificationChannels.Default, false),
        new(NotificationTemplateKeys.ApprovalTaskAssigned, NotificationCategory.Approval, NotificationChannels.Default, false),
        new(NotificationTemplateKeys.RequestApproved, NotificationCategory.Request, NotificationChannels.Default, false),
        new(NotificationTemplateKeys.RequestRejected, NotificationCategory.Request, NotificationChannels.Default, false),
    ];

    public static NotificationTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
}

/// <summary>Front-end routes used as notification links.</summary>
public static class NotificationLinks
{
    public const string Inbox = "/app/inbox";
    public const string MyRequests = "/app/requests";
}
