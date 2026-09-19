using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Unit tests for the SelfCheckIn audit-log aggregate (S-31).</summary>
public sealed class SelfCheckInTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();
    private static readonly DateTime Timestamp = new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RecordQr_SetsQrMethodAndNoCoordinates()
    {
        var checkIn = SelfCheckIn.RecordQr(_tenantId, _employeeId, Timestamp, PunchDirection.In, _locationId);

        checkIn.Method.ShouldBe(SelfCheckInMethod.Qr);
        checkIn.Direction.ShouldBe(PunchDirection.In);
        checkIn.LocationId.ShouldBe(_locationId);
        checkIn.Latitude.ShouldBeNull();
        checkIn.Longitude.ShouldBeNull();
        checkIn.WithinGeofence.ShouldBeNull();
        checkIn.TenantId.ShouldBe(_tenantId);
        checkIn.EmployeeId.ShouldBe(_employeeId);
        checkIn.TimestampUtc.ShouldBe(Timestamp);
    }

    [Fact]
    public void RecordGps_SetsGpsMethodAndCoordinates()
    {
        var checkIn = SelfCheckIn.RecordGps(_tenantId, _employeeId, Timestamp, PunchDirection.Out, _locationId, 39.9, 32.8, withinGeofence: true);

        checkIn.Method.ShouldBe(SelfCheckInMethod.Gps);
        checkIn.Direction.ShouldBe(PunchDirection.Out);
        checkIn.LocationId.ShouldBe(_locationId);
        checkIn.Latitude.ShouldBe(39.9);
        checkIn.Longitude.ShouldBe(32.8);
        checkIn.WithinGeofence.ShouldBe(true);
    }

    [Fact]
    public void RecordGps_WithoutLocation_AllowsNullLocationAndGeofence()
    {
        var checkIn = SelfCheckIn.RecordGps(_tenantId, _employeeId, Timestamp, PunchDirection.In, null, 39.9, 32.8, withinGeofence: null);

        checkIn.LocationId.ShouldBeNull();
        checkIn.WithinGeofence.ShouldBeNull();
    }
}
