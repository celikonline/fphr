namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Read-only cross-module lookup for a tenant's effective holiday calendar (roadmap Faz 0 item #5).
/// Mirrors the pattern this codebase already uses for cross-module reads (see IOrgUnitLookup,
/// Sense.Modules.Employee.Contracts.IEmployeeLookup). Implemented by
/// Sense.Modules.Organization.Infrastructure and registered by OrganizationModule.AddModule.
/// Future consumers: a home-page "resmi tatiller" widget (Faz 1+), Attendance's work-schedule engine
/// (Faz 2+), Leave's day-counting rules (Faz 1+) - none of which exist yet, so this port only exposes
/// what the design doc's 11.3.02-sirket-yapisi.md already calls for: "IHolidayCalendarLookup kontratı
/// (Leave gün sayımı, Attendance puantaj, ana sayfa)".
/// </summary>
public interface IHolidayCalendarLookup
{
    /// <summary>
    /// True if <paramref name="date"/> is a holiday under the tenant's effective calendar (the
    /// tenant's root Company.HolidayCalendarId, falling back to the tenant's seeded system TR calendar
    /// when no calendar is assigned). False (not an error) if the tenant has no calendar at all.
    /// </summary>
    Task<bool> IsHolidayAsync(Guid tenantId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upcoming holidays (today or later) under the tenant's effective calendar, ordered by date,
    /// nearest first, capped at <paramref name="count"/>. A recurring entry contributes its next
    /// occurrence (this year if not yet passed, otherwise next year); a non-recurring entry contributes
    /// its exact date only if it has not passed yet. Empty (not an error) if the tenant has no calendar.
    /// </summary>
    Task<IReadOnlyList<UpcomingHoliday>> GetUpcomingHolidaysAsync(Guid tenantId, int count = 10, CancellationToken cancellationToken = default);
}

/// <summary>One resolved upcoming holiday occurrence.</summary>
public sealed record UpcomingHoliday(DateOnly Date, string Name);
