using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Contracts.Events;

/// <summary>
/// Bir destek talebi oluşturulduğunda yayınlanır (11.3.13, roadmap #69). Sense.Worker'daki handler,
/// ISupportChannel üzerinden varsayılan e-posta adaptörüne (SupportOptions.SupportEmail) bildirim gönderir.
/// </summary>
public sealed record SupportTicketCreated(
    Guid TenantId, Guid TicketId, Guid RequestedByUserId, string Subject, string Category) : IntegrationEvent(TenantId);

/// <summary>Destek ekibi bir talebi yanıtladığında yayınlanır; talebi açan kullanıcıya bildirim için.</summary>
public sealed record SupportTicketAnswered(
    Guid TenantId, Guid TicketId, Guid RequestedByUserId, string Subject) : IntegrationEvent(TenantId);
