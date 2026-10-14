using Sense.Modules.Announcements.Application;
using Sense.Modules.Announcements.Application.Support;
using Sense.Modules.Announcements.Domain;
using Sense.Modules.Announcements.Contracts.Events;
using Sense.Shared.Contracts.Events;

namespace Sense.Modules.Announcements.Infrastructure.Support;

/// <summary>Sense.Worker'da çalışır: talep oluşturulunca ISupportChannel üzerinden bildirim gönderir (outbox -> OutboxProcessor -> IEventBus -> burada).</summary>
public sealed class SupportTicketCreatedHandler(ISupportChannel channel, ISupportPortalBridge bridge,
    ISupportTicketRepository tickets, IAnnouncementsUnitOfWork unitOfWork) : IIntegrationEventHandler<SupportTicketCreated>
{
    public async Task Handle(SupportTicketCreated integrationEvent, CancellationToken cancellationToken)
    {
        if (!bridge.Enabled)
        {
            await channel.NotifyTicketCreatedAsync(integrationEvent.TicketId, integrationEvent.Subject, integrationEvent.Category, cancellationToken);
            return;
        }
        var ticket = await tickets.GetByIdAsync(integrationEvent.TenantId, integrationEvent.TicketId, cancellationToken)
            ?? throw new InvalidOperationException("Support ticket unavailable for delivery.");
        if (ticket.ExternalRef?.StartsWith("fp-support:", StringComparison.Ordinal) == true) return;
        var remoteId = await bridge.CreateAsync(ticket, cancellationToken);
        ticket.AttachExternalRef($"fp-support:{remoteId:D}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Talep yanıtlanınca açan kullanıcıya bildirim gönderir.</summary>
public sealed class SupportTicketAnsweredHandler(ISupportChannel channel) : IIntegrationEventHandler<SupportTicketAnswered>
{
    public Task Handle(SupportTicketAnswered integrationEvent, CancellationToken cancellationToken) =>
        channel.NotifyTicketAnsweredAsync(integrationEvent.TicketId, integrationEvent.RequestedByUserId, integrationEvent.Subject, cancellationToken);
}
