using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Contracts;

/// <summary>
/// Template catalog keys (11.3.13). Texts live in Notifications.Infrastructure/Resources/NotificationTemplates{,.en,.de}.resx
/// as "{key}.title" / "{key}.body" with {Placeholder} arguments.
/// </summary>
public static class NotificationTemplateKeys
{
    /// <summary>To the requester: the request was submitted. Args: RequestType, RequesterName.</summary>
    public const string RequestCreated = "request.created";

    /// <summary>To an approver: a request waits for their decision. Args: RequestType, RequesterName.</summary>
    public const string ApprovalTaskAssigned = "approval.task_assigned";

    /// <summary>To the requester: every required step approved. Args: RequestType, RequesterName.</summary>
    public const string RequestApproved = "request.approved";

    /// <summary>To the requester: a required step rejected. Args: RequestType, RequesterName.</summary>
    public const string RequestRejected = "request.rejected";

    public static IReadOnlyList<string> All { get; } = [RequestCreated, ApprovalTaskAssigned, RequestApproved, RequestRejected];
}

/// <summary>Well-known template argument names.</summary>
public static class NotificationTemplateArgs
{
    public const string RequestType = "RequestType";
    public const string RequesterName = "RequesterName";
    public const string RecipientName = "RecipientName";
}

/// <summary>Delivery channels. Push (Expo/Web Push) is Faz 5.</summary>
[Flags]
public enum NotificationChannels
{
    None = 0,
    InApp = 1,
    Email = 2,
    Default = InApp | Email,
}

/// <summary>
/// One recipient. <paramref name="UserId"/> is required for in-app delivery and preferences; <paramref name="Email"/> is
/// required for e-mail delivery (Identity.Contracts has no user → e-mail lookup yet, so callers pass the address they know).
/// </summary>
public sealed record NotificationRecipient(Guid? UserId, string? Email, string? DisplayName);

/// <summary>
/// A request to notify recipients from a catalog template. <paramref name="Channels"/> is the caller's upper bound;
/// each recipient's NotificationPreference can switch a channel off.
/// </summary>
public sealed record NotificationRequest(
    Guid TenantId,
    IReadOnlyList<NotificationRecipient> Recipients,
    string TemplateKey,
    IReadOnlyDictionary<string, string> Args,
    string? Link = null,
    NotificationChannels Channels = NotificationChannels.Default);

/// <summary>What happened to a publish request (for logs/tests).</summary>
public sealed record NotificationPublishResult(int InAppCreated, int EmailsSent, int Skipped);

/// <summary>
/// Cross-module port for sending templated notifications (in-app + e-mail, SignalR push on creation). In-app rows are
/// persisted immediately in the Notifications schema (not in the caller's transaction) - prefer calling it from an
/// integration event handler so a rolled-back command never notifies anyone.
/// Implemented by Sense.Modules.Notifications.Infrastructure.
/// </summary>
public interface INotificationPublisher
{
    Task<Result<NotificationPublishResult>> PublishAsync(NotificationRequest request, CancellationToken ct = default);
}

/// <summary>SignalR client method names used for per-user notification pushes (hub: /hubs/notifications).</summary>
public static class NotificationHubMethods
{
    public const string NotificationCreated = "NotificationCreated";
}
