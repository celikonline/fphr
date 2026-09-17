using Sense.Modules.Announcements.Domain;
using Sense.Shared.Contracts.Persistence;

namespace Sense.Modules.Announcements.Application;

/// <summary>Announcements' SaveChanges port for integration event handlers, which run outside the command pipeline's automatic unit of work.</summary>
public interface IAnnouncementsUnitOfWork : IUnitOfWork;

/// <summary>Repository port for Announcement aggregate root.</summary>
public interface IAnnouncementRepository
{
    Task AddAsync(Announcement announcement, CancellationToken cancellationToken = default);

    Task<Announcement?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Announcement>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpdateAsync(Announcement announcement, CancellationToken cancellationToken = default);

    Task DeleteAsync(Announcement announcement, CancellationToken cancellationToken = default);
}

/// <summary>Repository port for AnnouncementReadRecord entity.</summary>
public interface IAnnouncementReadRecordRepository
{
    Task AddAsync(AnnouncementReadRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnnouncementReadRecord>> GetByAnnouncementIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default);

    Task<int> CountByAnnouncementIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default);
}
