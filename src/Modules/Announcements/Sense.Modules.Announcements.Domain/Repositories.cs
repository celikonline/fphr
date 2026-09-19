using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Domain;

/// <summary>Duyuru repository.</summary>
public interface IAnnouncementRepository
{
    void Add(Announcement announcement);

    Task<Announcement?> GetByIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Announcement>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Announcement>> GetPublishedAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Duyuru okunma kaydı repository (analytics).</summary>
public interface IAnnouncementReadRecordRepository
{
    void Add(AnnouncementReadRecord record);

    Task<bool> ExistsAsync(Guid tenantId, Guid announcementId, Guid employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnouncementReadRecord>> GetByAnnouncementIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default);

    Task<int> CountByAnnouncementIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default);
}

/// <summary>Support ticket repository ("Desteğe bağlan", roadmap #69).</summary>
public interface ISupportTicketRepository
{
    void Add(SupportTicket ticket);

    Task<SupportTicket?> GetByIdAsync(Guid tenantId, Guid ticketId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportTicket>> ListByRequesterAsync(Guid tenantId, Guid requestedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportTicket>> ListAsync(Guid tenantId, SupportTicketStatus? status, CancellationToken cancellationToken = default);
}

/// <summary>Yardım Merkezi arama günlüğü repository (content-as-code geri besleme sinyali).</summary>
public interface IHelpSearchLogRepository
{
    void Add(HelpSearchLog log);
}

/// <summary>Manually created company calendar entries (senseik-027, roadmap #71/P-19). Birthdays and leave starts
/// are derived at query time and never go through this repository.</summary>
public interface ICompanyEventRepository
{
    void Add(CompanyEvent companyEvent);

    void Remove(CompanyEvent companyEvent);

    Task<CompanyEvent?> GetByIdAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default);

    /// <summary>Every manual event whose EventDate falls in [from, to], regardless of visibility (callers filter).</summary>
    Task<IReadOnlyList<CompanyEvent>> GetInRangeAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
