using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>EF Core-backed Announcement repository (tenant isolation via ModuleDbContext's global query filter; TenantId kept explicit for clarity).</summary>
public sealed class AnnouncementRepository(AnnouncementsDbContext context) : IAnnouncementRepository
{
    public void Add(Announcement announcement) => context.Announcements.Add(announcement);

    public async Task<Announcement?> GetByIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default) =>
        await context.Announcements.FirstOrDefaultAsync(a => a.Id == announcementId && a.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<Announcement>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Announcements
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Announcement>> GetPublishedAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await context.Announcements
            .Where(a => a.TenantId == tenantId && a.Status == AnnouncementStatus.Published && (a.ExpiresAt == null || a.ExpiresAt > now))
            .OrderByDescending(a => a.PublishedAt)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed AnnouncementReadRecord repository.</summary>
public sealed class AnnouncementReadRecordRepository(AnnouncementsDbContext context) : IAnnouncementReadRecordRepository
{
    public void Add(AnnouncementReadRecord record) => context.AnnouncementReadRecords.Add(record);

    public async Task<bool> ExistsAsync(Guid tenantId, Guid announcementId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.AnnouncementReadRecords.AnyAsync(
            r => r.TenantId == tenantId && r.AnnouncementId == announcementId && r.EmployeeId == employeeId, cancellationToken);

    public async Task<IReadOnlyList<AnnouncementReadRecord>> GetByAnnouncementIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default) =>
        await context.AnnouncementReadRecords
            .Where(r => r.TenantId == tenantId && r.AnnouncementId == announcementId)
            .ToListAsync(cancellationToken);

    public async Task<int> CountByAnnouncementIdAsync(Guid tenantId, Guid announcementId, CancellationToken cancellationToken = default) =>
        await context.AnnouncementReadRecords
            .CountAsync(r => r.TenantId == tenantId && r.AnnouncementId == announcementId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetReadAnnouncementIdsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.AnnouncementReadRecords
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId)
            .Select(r => r.AnnouncementId)
            .ToListAsync(cancellationToken);
}
