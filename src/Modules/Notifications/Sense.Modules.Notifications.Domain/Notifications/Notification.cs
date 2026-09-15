using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Notifications.Domain.Notifications;

/// <summary>Business grouping of notification templates (drives preference screens and the catalog).</summary>
public enum NotificationCategory
{
    General = 0,
    Account = 1,
    Request = 2,
    Approval = 3,
    Schedule = 4,
    Asset = 5,
    Report = 6,
    Recruitment = 7,
    Security = 8,
}

/// <summary>
/// One in-app (home page / bell) notification for one user. Stores the template key and its arguments rather than
/// rendered text: users have no persisted culture yet and can switch language, so text is rendered per request culture.
/// </summary>
public sealed class Notification : TenantAggregateRoot<Guid>
{
    private Notification()
    {
    }

    private Notification(Guid id, Guid tenantId, Guid recipientUserId, string templateKey, NotificationCategory category, string argsJson, string? link, DateTime occurredAt)
        : base(id, tenantId)
    {
        RecipientUserId = recipientUserId;
        TemplateKey = templateKey;
        Category = category;
        ArgsJson = argsJson;
        Link = link;
        OccurredAt = occurredAt;
    }

    public Guid RecipientUserId { get; private set; }

    public string TemplateKey { get; private set; } = string.Empty;

    public NotificationCategory Category { get; private set; }

    /// <summary>JSON object of template placeholder values ({"RequestType":"Leave", ...}).</summary>
    public string ArgsJson { get; private set; } = NotificationLimits.EmptyArgsJson;

    /// <summary>Relative front-end link ("/app/inbox") opened when the notification is clicked.</summary>
    public string? Link { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public DateTime? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public static Result<Notification> Create(
        Guid tenantId, Guid recipientUserId, string templateKey, NotificationCategory category, string? argsJson, string? link, DateTime occurredAt)
    {
        if (recipientUserId == Guid.Empty)
        {
            return Error.Validation(NotificationsDomainErrors.RecipientRequired);
        }

        if (string.IsNullOrWhiteSpace(templateKey) || templateKey.Length > NotificationLimits.TemplateKeyMaxLength)
        {
            return Error.Validation(NotificationsDomainErrors.TemplateKeyInvalid);
        }

        if (link is { Length: > NotificationLimits.LinkMaxLength })
        {
            return Error.Validation(NotificationsDomainErrors.LinkTooLong);
        }

        return new Notification(
            Guid.CreateVersion7(), tenantId, recipientUserId, templateKey, category,
            string.IsNullOrWhiteSpace(argsJson) ? NotificationLimits.EmptyArgsJson : argsJson, link, occurredAt);
    }

    /// <summary>Idempotent: re-reading keeps the first read timestamp.</summary>
    public void MarkRead(DateTime readAt) => ReadAt ??= readAt;
}
