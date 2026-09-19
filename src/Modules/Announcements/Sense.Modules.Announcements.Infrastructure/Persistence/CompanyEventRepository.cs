using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>EF Core-backed CompanyEvent repository (senseik-027; tenant isolation via ModuleDbContext's global query filter).</summary>
public sealed class CompanyEventRepository(AnnouncementsDbContext context) : ICompanyEventRepository
{
    public void Add(CompanyEvent companyEvent) => context.CompanyEvents.Add(companyEvent);

    public void Remove(CompanyEvent companyEvent) => context.CompanyEvents.Remove(companyEvent);

    public async Task<CompanyEvent?> GetByIdAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        await context.CompanyEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<CompanyEvent>> GetInRangeAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await context.CompanyEvents
            .Where(e => e.TenantId == tenantId && e.EventDate >= from && e.EventDate <= to)
            .OrderBy(e => e.EventDate)
            .ToListAsync(cancellationToken);
}
