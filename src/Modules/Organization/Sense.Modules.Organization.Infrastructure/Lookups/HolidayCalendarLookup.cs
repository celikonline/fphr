using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.HolidayCalendars;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>
/// Implements Organization.Contracts.IHolidayCalendarLookup: resolves a tenant's effective calendar
/// (Company.HolidayCalendarId, falling back to the tenant's seeded system TR calendar - see
/// docs/analysis/kolayik-video/11.3.02-sirket-yapisi.md: "çözümleme: çalışanın Company ataması →
/// Company.HolidayCalendarId → ... varsayılan") and answers holiday-membership/upcoming-list queries
/// against it. Faz 0 has exactly one Company per tenant (see Company's own doc comment), so "the
/// tenant's Company" is unambiguous for now; a future multi-Company tenant would need this to take an
/// explicit Company/OrgUnit scope instead of just a TenantId.
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
            .Select(e => new UpcomingHoliday(NextOccurrence(e, today), e.Name))
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

    private async Task<Guid?> ResolveCalendarIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var company = await context.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (company?.HolidayCalendarId is { } assigned)
        {
            return assigned;
        }

        // Defensive fallback: DataSeeder always assigns the seeded TR calendar to the tenant's Company
        // at creation time, so this branch is normally unreachable, but a tenant provisioned outside
        // that path (or with the assignment later cleared) still falls back to its own system calendar.
        var systemCalendar = await context.HolidayCalendars.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsSystemDefined && c.CountryCode == "TR")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return systemCalendar;
    }
}
