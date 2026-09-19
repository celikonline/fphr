using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Attendance.Domain.SelfCheckIns;

/// <summary>"in" (giriş) or "out" (çıkış) - same two-value shape as the PDKS device log's own direction, kept as a
/// separate Domain-layer enum since <c>DeviceLogDirection</c> lives in the Application layer's DeviceLogs feature
/// and Domain types must not depend on Application.</summary>
public enum PunchDirection
{
    In = 0,
    Out = 1,
}

/// <summary>How the self-servis check-in was verified (S-31 kabul kriteri 1/2).</summary>
public enum SelfCheckInMethod
{
    /// <summary>Location's printed QR code was scanned; coordinates (and a geofence verdict) are recorded too when the location has a radius.</summary>
    Qr = 0,

    /// <summary>The caller's device GPS coordinates were submitted, optionally validated against a Location's geofence.</summary>
    Gps = 1,
}

/// <summary>
/// Audit trail of one self-servis QR/GPS check-in/out attempt (S-31). Kept separate from
/// <see cref="AttendanceDays.AttendanceDay"/> (one row per employee+day summary) so every individual punch - with
/// its method, resolved location and raw GPS coordinates - stays inspectable even though only the
/// earliest-in/latest-out pair ever reaches the AttendanceDay row (same "giriş kaydı oluştur" upsert every other
/// channel uses, see Application/AttendanceDays/AttendanceDayUpsert.cs).
/// Write-once: nothing about a past attempt is ever corrected in place.
/// </summary>
public sealed class SelfCheckIn : TenantAggregateRoot<Guid>
{
    private SelfCheckIn()
    {
    }

    private SelfCheckIn(
        Guid id, Guid tenantId, Guid employeeId, DateTime timestampUtc, PunchDirection direction, SelfCheckInMethod method,
        Guid? locationId, double? latitude, double? longitude, bool? withinGeofence)
        : base(id, tenantId)
    {
        EmployeeId = employeeId;
        TimestampUtc = timestampUtc;
        Direction = direction;
        Method = method;
        LocationId = locationId;
        Latitude = latitude;
        Longitude = longitude;
        WithinGeofence = withinGeofence;
    }

    public Guid EmployeeId { get; private set; }

    public DateTime TimestampUtc { get; private set; }

    public PunchDirection Direction { get; private set; }

    public SelfCheckInMethod Method { get; private set; }

    /// <summary>Organization.Location this check-in resolved to; null for a GPS check-in with no target location (saha/uzaktan çalışan, roaming).</summary>
    public Guid? LocationId { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    /// <summary>Null when the location had no geofence configured (not checked); true/false is the actual distance verdict.</summary>
    public bool? WithinGeofence { get; private set; }

    public static SelfCheckIn RecordQr(
        Guid tenantId, Guid employeeId, DateTime timestampUtc, PunchDirection direction, Guid locationId,
        double? latitude = null, double? longitude = null, bool? withinGeofence = null) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, timestampUtc, direction, SelfCheckInMethod.Qr, locationId, latitude, longitude, withinGeofence);

    public static SelfCheckIn RecordGps(
        Guid tenantId, Guid employeeId, DateTime timestampUtc, PunchDirection direction, Guid? locationId,
        double latitude, double longitude, bool? withinGeofence) =>
        new(Guid.CreateVersion7(), tenantId, employeeId, timestampUtc, direction, SelfCheckInMethod.Gps, locationId, latitude, longitude, withinGeofence);
}
