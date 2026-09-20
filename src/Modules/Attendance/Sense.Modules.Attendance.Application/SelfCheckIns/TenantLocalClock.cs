using Sense.Modules.Identity.Contracts;

namespace Sense.Modules.Attendance.Application.SelfCheckIns;

/// <summary>One instant expressed both ways: <paramref name="Utc"/> for the audit row, <paramref name="Local"/> (tenant wall-clock) for the AttendanceDay date/time.</summary>
public sealed record TenantLocalNow(DateTime Utc, DateTime Local);

/// <summary>
/// "Now" in the tenant's own time zone (S-31). AttendanceDay check-in/out are wall-clock <c>TimeOnly</c> values compared to a local
/// work schedule, so a self-servis punch must NOT be stamped with the UTC time-of-day (Istanbul is UTC+3: a 09:00 arrival would
/// otherwise be recorded as 06:00 and flagged as an anomaly). The zone comes from Identity.Contracts.ITenantTimeZoneLookup.
/// </summary>
public interface ITenantLocalClock
{
    Task<TenantLocalNow> NowAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed class TenantLocalClock(ITenantTimeZoneLookup timeZones, TimeProvider clock) : ITenantLocalClock
{
    public async Task<TenantLocalNow> NowAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var utc = clock.GetUtcNow().UtcDateTime;
        var zoneId = await timeZones.FindTimeZoneIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var zone = zoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var found) ? found : TimeZoneInfo.Utc;
        return new TenantLocalNow(utc, TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    }
}
