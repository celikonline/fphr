using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>EF Core-backed Announcement repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class AnnouncementRepository(AnnouncementsDbContext context) : IAnnouncementRepository
{
    public async Task AddAsync(Announcement announcement, CancellationToken ct = default)
    {
        context.Announcements.Add(announcement);
        await Task.CompletedTask;
    }

    public async Task<Announcement?> GetByIdAsync(Guid tenantId, Guid announcementId, CancellationToken ct = default)
    {
        return await context.Announcements
            .FirstOrDefaultAsync(a => a.Id == announcementId && a.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<Announcement>> GetAllAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.Announcements
            .Where(a => a.TenantId == tenantId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Announcement>> GetPublishedAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await context.Announcements
            .Where(a => a.TenantId == tenantId && a.Status == AnnouncementStatus.Published)
            .ToListAsync(ct);
    }
}
