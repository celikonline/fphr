using Sense.Modules.Identity.Contracts;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

/// <summary>Timed events are stored as UTC instants; their calendar day is the tenant's local day (B14).
/// All-day events are floating dates and never go through here.</summary>
public static class CompanyEventLocalDay
{
    public static async Task<TimeZoneInfo> ZoneAsync(ITenantTimeZoneLookup timeZones, Guid tenantId, CancellationToken cancellationToken)
    {
        var zoneId = await timeZones.FindTimeZoneIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrEmpty(zoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;
    }

    public static DateOnly LocalDate(DateTime utcInstant, TimeZoneInfo? zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), zone ?? TimeZoneInfo.Utc));
}
