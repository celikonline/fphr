using Microsoft.EntityFrameworkCore;
using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Infrastructure.Persistence;

/// <summary>EF Core-backed CompanyEvent repository (senseik-027; tenant isolation via ModuleDbContext's global query filter).</summary>
public sealed class CompanyEventRepository(AnnouncementsDbContext context) : ICompanyEventRepository
{
    public void Add(CompanyEvent companyEvent) => context.CompanyEvents.Add(companyEvent);

    public void Remove(CompanyEvent companyEvent) => context.CompanyEvents.Remove(companyEvent);

    public async Task<CompanyEvent?> GetByIdAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        await context.CompanyEvents.Include(e => e.Invitees).FirstOrDefaultAsync(e => e.Id == eventId && e.TenantId == tenantId, cancellationToken);

    /// <summary>Events overlapping [from-1d, to+1d] (padded for tenant-local days; see below): start before the day after <paramref name="to"/> and end (or start, when open-ended) on/after <paramref name="from"/>.
    /// Rows not yet backfilled (StartsAt null) fall back to EventDate.</summary>
    public async Task<IReadOnlyList<CompanyEvent>> GetInRangeAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        // Timed events overlap by tenant-local day, so pad one day each side (zone offsets are < 24h); the query handler trims exactly.
        var paddedFrom = from.AddDays(-1);
        var paddedTo = to.AddDays(1);
        var fromStart = DateTime.SpecifyKind(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toExclusive = DateTime.SpecifyKind(to.AddDays(2).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        return await context.CompanyEvents
            .Include(e => e.Invitees)
            .Where(e => e.TenantId == tenantId
                && (e.StartsAt == null
                    ? e.EventDate >= paddedFrom && e.EventDate <= paddedTo
                    : e.StartsAt < toExclusive && (e.EndsAt ?? e.StartsAt) >= fromStart))
            .OrderBy(e => e.EventDate)
            .ThenBy(e => e.StartsAt)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>EF Core-backed company event category repository (E4).</summary>
public sealed class CompanyEventCategoryRepository(AnnouncementsDbContext context) : ICompanyEventCategoryRepository
{
    public void Add(CompanyEventCategory category) => context.CompanyEventCategories.Add(category);

    public void Remove(CompanyEventCategory category) => context.CompanyEventCategories.Remove(category);

    public async Task<CompanyEventCategory?> GetByIdAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken = default) =>
        await context.CompanyEventCategories.FirstOrDefaultAsync(c => c.Id == categoryId && c.TenantId == tenantId, cancellationToken);

    public async Task<IReadOnlyList<CompanyEventCategory>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.CompanyEventCategories.Where(c => c.TenantId == tenantId).OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public async Task<bool> IsInUseAsync(Guid tenantId, Guid categoryId, CancellationToken cancellationToken = default) =>
        await context.CompanyEvents.AnyAsync(e => e.TenantId == tenantId && e.CategoryId == categoryId, cancellationToken);
}
