using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Contracts.Events;

/// <summary>
/// Raised when a company event with "notify invitees" on gains invitees (creation, or invitees added by an update):
/// Notifications sends <c>announcement.event_invitation</c> to <paramref name="EmployeeIds"/> only (E4).
/// <paramref name="StartsAt"/>/<paramref name="EndsAt"/> are wall-clock values; the date alone matters when
/// <paramref name="IsAllDay"/> is true. <paramref name="Location"/> (onsite) or <paramref name="OnlineUrl"/> (online)
/// may be null.
/// </summary>
public sealed record CompanyEventInvitationRequested(
    Guid TenantId,
    Guid CompanyEventId,
    string Title,
    DateTime StartsAt,
    DateTime? EndsAt,
    bool IsAllDay,
    string? Location,
    string? OnlineUrl,
    IReadOnlyList<Guid> EmployeeIds) : IntegrationEvent(TenantId);
