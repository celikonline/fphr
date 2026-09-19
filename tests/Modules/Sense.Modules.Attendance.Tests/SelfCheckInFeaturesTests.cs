using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Unit tests for QR/GPS self-servis check-in and "şu an içeride miyim" (S-31 kabul kriteri 1/2).</summary>
public sealed class SelfCheckInFeaturesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _locationId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILocationLookup _locations = Substitute.For<ILocationLookup>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly ISelfCheckInRepository _selfCheckIns = Substitute.For<ISelfCheckInRepository>();
    private static readonly DateOnly Today = new(2026, 9, 21);
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 9, 21, 9, 0, 0, TimeSpan.Zero));

    public SelfCheckInFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _schedules.GetForDateAsync(_employeeId, Today, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
    }

    private QrCheckInHandler CreateQrHandler() => new(_tenant, _currentUser, _locations, _days, _schedules, _selfCheckIns, Clock);

    private GpsCheckInHandler CreateGpsHandler() => new(_tenant, _currentUser, _locations, _days, _schedules, _selfCheckIns, Clock);

    [Fact]
    public async Task QrCheckIn_ValidCode_RecordsCheckInAndReturnsLocation()
    {
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", null, null, null));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Direction.ShouldBe(PunchDirection.In);
        result.Value.LocationId.ShouldBe(_locationId);
        result.Value.LocationName.ShouldBe("İstanbul HQ");
        result.Value.WithinGeofence.ShouldBeNull();
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.Source == AttendanceDaySource.SelfService && d.CheckIn == new TimeOnly(9, 0)));
        _selfCheckIns.Received(1).Add(Arg.Is<SelfCheckIn>(c => c.Method == SelfCheckInMethod.Qr && c.LocationId == _locationId));
    }

    [Fact]
    public async Task QrCheckIn_UnknownCode_ReturnsNotFound()
    {
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "BADCODE1", Arg.Any<CancellationToken>()).Returns((LocationGeofence?)null);

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("BADCODE1", PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInInvalidCode);
        _selfCheckIns.DidNotReceive().Add(Arg.Any<SelfCheckIn>());
    }

    [Fact]
    public async Task QrCheckIn_NoLinkedEmployee_ReturnsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task QrCheckIn_SecondInPunchSameDay_KeepsFirstCheckIn()
    {
        var existing = AttendanceDay.Record(_tenantId, _employeeId, Today, new TimeOnly(8, 30), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", null, null, null));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34", PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.CheckIn.ShouldBe(new TimeOnly(8, 30));
    }

    [Fact]
    public async Task QrCheckIn_OutPunch_OverwritesCheckOutToNow()
    {
        var existing = AttendanceDay.Record(_tenantId, _employeeId, Today, new TimeOnly(8, 30), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", null, null, null));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34", PunchDirection.Out), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.CheckOut.ShouldBe(new TimeOnly(9, 0));
    }

    [Fact]
    public async Task GpsCheckIn_NoLocationGiven_SucceedsWithoutGeofenceCheck()
    {
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var result = await CreateGpsHandler().Handle(new GpsCheckInCommand(39.9, 32.8, null, PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WithinGeofence.ShouldBeNull();
        result.Value.LocationId.ShouldBeNull();
    }

    [Fact]
    public async Task GpsCheckIn_LocationNotFound_ReturnsNotFound()
    {
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>()).Returns((LocationGeofence?)null);

        var result = await CreateGpsHandler().Handle(new GpsCheckInCommand(39.9, 32.8, _locationId, PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInLocationNotFound);
    }

    [Fact]
    public async Task GpsCheckIn_WithinGeofence_Succeeds()
    {
        // ~11 m away from the geofence center (well inside a 150 m radius).
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var result = await CreateGpsHandler().Handle(new GpsCheckInCommand(39.925600, 32.866300, _locationId, PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WithinGeofence.ShouldBe(true);
    }

    [Fact]
    public async Task GpsCheckIn_OutsideGeofence_RejectsWithoutTouchingAttendanceDay()
    {
        // ~1.1 km away from the geofence center - well outside a 150 m radius.
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));

        var result = await CreateGpsHandler().Handle(new GpsCheckInCommand(39.935533, 32.866287, _locationId, PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInOutsideGeofence);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
        _selfCheckIns.DidNotReceive().Add(Arg.Any<SelfCheckIn>());
    }

    [Fact]
    public async Task GpsCheckIn_LocationWithoutGeofenceConfigured_SkipsDistanceCheck()
    {
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "Uzak Ofis", null, null, null));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var result = await CreateGpsHandler().Handle(new GpsCheckInCommand(0, 0, _locationId, PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WithinGeofence.ShouldBeNull();
    }

    [Fact]
    public async Task GetMyCheckInStatus_CheckedInWithNoCheckOut_ReportsInside()
    {
        var existing = AttendanceDay.Record(_tenantId, _employeeId, Today, new TimeOnly(9, 0), null, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns(existing);
        var handler = new GetMyCheckInStatusHandler(_currentUser, _days, Clock);

        var result = await handler.Handle(new GetMyCheckInStatusQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsCheckedIn.ShouldBeTrue();
    }

    [Fact]
    public async Task GetMyCheckInStatus_NoRecordToday_ReportsNotCheckedIn()
    {
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        var handler = new GetMyCheckInStatusHandler(_currentUser, _days, Clock);

        var result = await handler.Handle(new GetMyCheckInStatusQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsCheckedIn.ShouldBeFalse();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
