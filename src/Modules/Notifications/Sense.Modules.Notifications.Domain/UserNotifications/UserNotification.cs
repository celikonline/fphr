using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Notifications.Domain.UserNotifications;

/// <summary>
/// In-app (home page / bell) notification for one user, already rendered in the recipient's language when created.
/// <see cref="SourceEventId"/> + <see cref="TemplateKey"/> + <see cref="UserId"/> is unique, so re-delivered
/// integration events do not duplicate it.
/// </summary>
public sealed class UserNotification : TenantEntity<Guid>
{
    private UserNotification()
    {
    }

    public UserNotification(Guid tenantId, Guid userId, string templateKey, string title, string body, string? actionUrl, Guid sourceEventId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        UserId = userId;
        TemplateKey = Guard.MaxLength(templateKey, NotificationsLimits.TemplateKeyMaxLength);
        Title = Truncate(title, NotificationsLimits.TitleMaxLength);
        Body = Truncate(body, NotificationsLimits.BodyMaxLength);
        ActionUrl = string.IsNullOrWhiteSpace(actionUrl) ? null : Guard.MaxLength(actionUrl, NotificationsLimits.ActionUrlMaxLength);
        SourceEventId = sourceEventId;
    }

    public Guid UserId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>App-relative path (e.g. "/app/inbox") the notification opens, if any.</summary>
    public string? ActionUrl { get; private set; }

    public Guid SourceEventId { get; private set; }

    public DateTime? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>Idempotent: keeps the first read time.</summary>
    public void MarkRead(DateTime now) => ReadAt ??= now;

    // Rendered text comes from templates + user-entered values (names); clip instead of failing the whole event.
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
