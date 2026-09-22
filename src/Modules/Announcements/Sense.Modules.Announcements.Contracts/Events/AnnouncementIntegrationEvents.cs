using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Contracts.Events;

/// <summary>
/// Raised when an announcement is published with "notify employees" on: Notifications sends every active employee an
/// e-mail and an in-app notification. <paramref name="Excerpt"/> is plain text (HTML stripped, shortened) - the full
/// content stays in the app. <paramref name="IsHighPriority"/> marks urgent announcements in the subject.
/// </summary>
public sealed record AnnouncementPublished(
    Guid TenantId, Guid AnnouncementId, string Title, string Excerpt, bool IsHighPriority) : IntegrationEvent(TenantId);
