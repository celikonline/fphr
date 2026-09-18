using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Announcements.Application;

/// <summary>Announcements' SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface IAnnouncementsUnitOfWork : IUnitOfWork;

/// <summary>
/// "Desteğe bağlan" kanal adaptörü (11.3.13: ISupportChannel{OpenConversation, CreateTicket}). Bu paket yalnızca
/// bildirim gönderimini kapsar (OpenConversation/canlı sohbet Faz 8 kararı); varsayılan adaptör e-posta.
/// </summary>
public interface ISupportChannel
{
    /// <summary>Yeni talep açıldığında Sense destek ekibine bildirim gönderir.</summary>
    Task NotifyTicketCreatedAsync(Guid ticketId, string subject, string category, CancellationToken cancellationToken = default);

    /// <summary>Talep yanıtlandığında talebi açan kullanıcıya bildirim gönderir.</summary>
    Task NotifyTicketAnsweredAsync(Guid ticketId, Guid requestedByUserId, string subject, CancellationToken cancellationToken = default);
}
