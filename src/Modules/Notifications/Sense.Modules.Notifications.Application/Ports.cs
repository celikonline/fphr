using Sense.Modules.Notifications.Application.UserNotifications;

namespace Sense.Modules.Notifications.Application;

/// <summary>Rendered template output. Email: Subject + plain-text Body + HtmlBody. In-app: Subject is the title, Body plain text.</summary>
public sealed record RenderedNotification(string Subject, string Body, string? HtmlBody);

/// <summary>
/// Renders "{key}.{channel}.{lang}" templates (Scriban, embedded in Infrastructure). Unsupported/missing languages
/// fall back to the default language. Model keys are snake_case template variables.
/// </summary>
public interface INotificationTemplateRenderer
{
    RenderedNotification Render(string templateKey, string channel, string language, IReadOnlyDictionary<string, object?> model);
}

/// <summary>Pushes a freshly created in-app notification to the user's open sessions. Best-effort: never throws.</summary>
public interface IRealtimeNotifier
{
    Task NotifyUserAsync(Guid userId, UserNotificationDto notification, CancellationToken ct = default);
}

/// <summary>
/// Template-specific model enrichment right before an e-mail is sent (e.g. minting the approval decision link).
/// Returning null skips the delivery with the given reason.
/// </summary>
public interface IEmailModelEnricher
{
    string TemplateKey { get; }

    Task<EnrichmentResult> EnrichAsync(Guid tenantId, Dictionary<string, object?> model, CancellationToken ct = default);
}

public sealed record EnrichmentResult(bool Skip, string? SkipReason)
{
    public static EnrichmentResult Continue { get; } = new(false, null);

    public static EnrichmentResult SkipWith(string reason) => new(true, reason);
}

/// <summary>Module settings ("Notifications" section).</summary>
public sealed class NotificationsOptions
{
    public const string Section = "Notifications";

    /// <summary>Used when the recipient has no preferred language.</summary>
    public string DefaultLanguage { get; set; } = "tr";

    public DeliveryOptions Delivery { get; set; } = new();

    public sealed class DeliveryOptions
    {
        public int BatchSize { get; set; } = 20;

        public int MaxAttempts { get; set; } = 8;

        public int BaseBackoffSeconds { get; set; } = 30;

        public int MaxBackoffSeconds { get; set; } = 3600;

        public int PollingIntervalSeconds { get; set; } = 5;
    }
}
