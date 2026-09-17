using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>EF Core-backed AnnouncementReadRecord repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class AnnouncementReadRecordRepository(AnnouncementsDbContext context) : IAnnouncementReadRecordRepository
{
    public async Task AddAsync(AnnouncementReadRecord record, CancellationToken ct = default)
    {
        context.AnnouncementReadRecords.Add(record);
        await Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AnnouncementReadRecord>> GetByEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken ct = default)
    {
        return await context.AnnouncementReadRecords
            .Where(r => r.TenantId == tenantId && r.EmployeeId == employeeId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AnnouncementReadRecord>> GetByAnnouncementAsync(Guid tenantId, Guid announcementId, CancellationToken ct = default)
    {
        return await context.AnnouncementReadRecords
            .Where(r => r.TenantId == tenantId && r.AnnouncementId == announcementId)
            .ToListAsync(ct);
    }
}
