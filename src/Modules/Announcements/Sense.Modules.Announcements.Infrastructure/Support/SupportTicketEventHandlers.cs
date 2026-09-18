using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Infrastructure.Support;

/// <summary>Sense.Worker'da çalışır: talep oluşturulunca ISupportChannel üzerinden bildirim gönderir (outbox -> OutboxProcessor -> IEventBus -> burada).</summary>
public sealed class SupportTicketCreatedHandler(ISupportChannel channel) : IIntegrationEventHandler<SupportTicketCreated>
{
    public Task Handle(SupportTicketCreated integrationEvent, CancellationToken cancellationToken) =>
        channel.NotifyTicketCreatedAsync(integrationEvent.TicketId, integrationEvent.Subject, integrationEvent.Category, cancellationToken);
}

/// <summary>Talep yanıtlanınca açan kullanıcıya bildirim gönderir.</summary>
public sealed class SupportTicketAnsweredHandler(ISupportChannel channel) : IIntegrationEventHandler<SupportTicketAnswered>
{
    public Task Handle(SupportTicketAnswered integrationEvent, CancellationToken cancellationToken) =>
        channel.NotifyTicketAnsweredAsync(integrationEvent.TicketId, integrationEvent.RequestedByUserId, integrationEvent.Subject, cancellationToken);
}
