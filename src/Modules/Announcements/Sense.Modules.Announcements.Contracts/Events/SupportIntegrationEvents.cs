using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Contracts.Events;

/// <summary>
/// Published when a user opens a support ticket ("Desteğe bağlan"; 11.3.13 event "Support.TicketCreated"). No consumer
/// yet - a future platform support queue / Notifications template can subscribe. Category/Channel are the enum names.
/// </summary>
public sealed record SupportTicketCreated(
    Guid TenantId, Guid TicketId, Guid UserId, string Subject, string Category, string Channel, string? PageContext)
    : IntegrationEvent(TenantId);
