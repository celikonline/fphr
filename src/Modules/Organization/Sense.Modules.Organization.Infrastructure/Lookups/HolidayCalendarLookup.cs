using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Domain.OrgUnitTypes;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>
/// Implements Organization.Contracts.IHolidayCalendarLookup: resolves a tenant's effective calendar
/// (Company.HolidayCalendarId, falling back to the tenant's seeded system TR calendar - see
/// docs/analysis/kolayik-video/11.3.02-sirket-yapisi.md: "çözümleme: çalışanın Company ataması →
/// Company.HolidayCalendarId → ... varsayılan") and answers holiday-membership/upcoming-list queries
/// against it. The tenant-only overloads use the tenant default; <see cref="ResolveCalendarAsync"/> takes an
/// explicit Company or OrgUnit scope for multi-company tenants.
/// </summary>
public sealed class HolidayCalendarLookup(OrganizationDbContext context) : IHolidayCalendarLookup
{
    public async Task<bool> IsHolidayAsync(Guid tenantId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var calendarId = await ResolveCalendarIdAsync(tenantId, cancellationToken);
        if (calendarId is null)
        {
            return false;
        }

        var entries = await context.HolidayCalendarEntries
            .Where(e => e.TenantId == tenantId && e.HolidayCalendarId == calendarId)
            .ToListAsync(cancellationToken);

        return entries.Any(e => e.MatchesDate(date));
    }

    public async Task<IReadOnlyList<UpcomingHoliday>> GetUpcomingHolidaysAsync(Guid tenantId, int count = 10, CancellationToken cancellationToken = default)
    {
        var calendarId = await ResolveCalendarIdAsync(tenantId, cancellationToken);
        if (calendarId is null)
        {
            return [];
        }

        var entries = await context.HolidayCalendarEntries
            .Where(e => e.TenantId == tenantId && e.HolidayCalendarId == calendarId)
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return entries
            .Select(e => new UpcomingHoliday(NextOccurrence(e, today), e.Name, e.IsHalfDay))
            .Where(h => h.Date >= today)
            .OrderBy(h => h.Date)
            .Take(count)
            .ToList();
    }

    /// <summary>For a recurring entry, the next Month/Day occurrence on/after today (this year, or next year if this year's has passed); for a non-recurring entry, its exact stored date.</summary>
    private static DateOnly NextOccurrence(HolidayCalendarEntry entry, DateOnly today)
    {
        if (!entry.IsRecurringAnnually)
        {
            return entry.Date;
        }

        var thisYear = new DateOnly(today.Year, entry.Date.Month, entry.Date.Day);
        return thisYear >= today ? thisYear : thisYear.AddYears(1);
    }

    public async Task<HolidayCalendarRef?> ResolveCalendarAsync(Guid tenantId, Guid? companyId, Guid? orgUnitId, CancellationToken cancellationToken = default)
    {
        Guid? calendarId = null;
        if (companyId is { } id)
        {
            calendarId = await context.Companies.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Id == id)
                .Select(c => c.HolidayCalendarId)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (orgUnitId is { } unitId && await FindCompanyUnitIdAsync(tenantId, unitId, cancellationToken) is { } companyUnitId)
        {
            calendarId = await context.Companies.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.OrgUnitId == companyUnitId)
                .Select(c => c.HolidayCalendarId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        calendarId ??= await ResolveCalendarIdAsync(tenantId, cancellationToken);
        if (calendarId is null)
        {
            return null;
        }

        return await context.HolidayCalendars.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == calendarId)
            .Select(c => new HolidayCalendarRef(c.Id, c.Name))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UpcomingHoliday>> GetHolidaysAsync(Guid tenantId, Guid holidayCalendarId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var entries = await context.HolidayCalendarEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.HolidayCalendarId == holidayCalendarId)
            .ToListAsync(cancellationToken);

        return entries
            .SelectMany(e => e.OccurrencesBetween(from, to).Select(date => new UpcomingHoliday(date, e.Name, e.IsHalfDay)))
            .OrderBy(h => h.Date)
            .ThenBy(h => h.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Walks parent links from the unit up to the nearest Company-level unit (inactive units included).</summary>
    private async Task<Guid?> FindCompanyUnitIdAsync(Guid tenantId, Guid orgUnitId, CancellationToken cancellationToken)
    {
        var companyTypeIds = await context.OrgUnitTypes.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Code == OrgUnitTypeCodes.Company)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var units = await context.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new { u.Id, u.ParentId, u.TypeId })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var visited = new HashSet<Guid>();
        Guid? current = orgUnitId;
        while (current is { } id && visited.Add(id) && units.TryGetValue(id, out var unit))
        {
            if (companyTypeIds.Contains(unit.TypeId))
            {
                return unit.Id;
            }

            current = unit.ParentId;
        }

        return null;
    }

    public async Task<int?> GetLastDefinedReligiousHolidayYearAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var calendarId = await ResolveCalendarIdAsync(tenantId, cancellationToken);
        if (calendarId is null)
        {
            return null;
        }

        // Nullable projection so an empty set yields null instead of throwing (single MAX in SQL, no row load).
        return await context.HolidayCalendarEntries.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.HolidayCalendarId == calendarId && !e.IsRecurringAnnually)
            .Select(e => (int?)e.Date.Year)
            .MaxAsync(cancellationToken);
    }

    /// <summary>Tenant default: the earliest company with an assigned calendar, else the tenant's system TR calendar.</summary>
    private async Task<Guid?> ResolveCalendarIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var assigned = await context.Companies.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.HolidayCalendarId != null)
            .OrderBy(c => c.Id)
            .Select(c => c.HolidayCalendarId)
            .FirstOrDefaultAsync(cancellationToken);

        if (assigned is not null)
        {
            return assigned;
        }

        return await context.HolidayCalendars.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsSystemDefined && c.CountryCode == "TR")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
