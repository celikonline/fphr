using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
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
    private readonly IShiftSettingsRepository _shiftSettings = Substitute.For<IShiftSettingsRepository>();
    private readonly ISelfCheckInRepository _selfCheckIns = Substitute.For<ISelfCheckInRepository>();
    private readonly List<SelfCheckIn> _punches = [];
    private AttendanceDay? _todayRecord;
    private static readonly DateOnly Today = new(2026, 9, 21);
    private static readonly FixedLocalClock Clock = new(new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc));

    public SelfCheckInFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _schedules.GetForDateAsync(_employeeId, Today, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), null, null, true));
    }

    private QrCheckInHandler CreateQrHandler() => new(_tenant, _currentUser, _locations, _days, _schedules, _shiftSettings, _selfCheckIns, Clock);

    private GpsCheckInHandler CreateGpsHandler() => new(_tenant, _currentUser, _locations, _days, _schedules, _shiftSettings, _selfCheckIns, Clock);

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
    public async Task QrCheckIn_LocationWithRadius_WithoutCoordinates_IsRejected()
    {
        // Static QR poster + geofence: a forwarded photo of the poster must not be enough.
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34EF56", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34EF56", PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInCoordinatesRequired);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
        _selfCheckIns.DidNotReceive().Add(Arg.Any<SelfCheckIn>());
    }

    [Fact]
    public async Task QrCheckIn_LocationWithRadius_FarAway_IsRejected()
    {
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34EF56", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34EF56", PunchDirection.In, 39.935533, 32.866287), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInOutsideGeofence);
        _selfCheckIns.DidNotReceive().Add(Arg.Any<SelfCheckIn>());
    }

    [Fact]
    public async Task QrCheckIn_LocationWithRadius_InsideFence_RecordsCoordinatesAndVerdict()
    {
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34EF56", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);

        var result = await CreateQrHandler().Handle(new QrCheckInCommand("AB12CD34EF56", PunchDirection.In, 39.925600, 32.866300), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WithinGeofence.ShouldBe(true);
        _selfCheckIns.Received(1).Add(Arg.Is<SelfCheckIn>(c => c.Method == SelfCheckInMethod.Qr && c.WithinGeofence == true && c.Latitude == 39.9256));
    }

    [Fact]
    public async Task QrCheckIn_StampsTenantLocalTime_NotUtc()
    {
        // 06:00 UTC = 09:00 Istanbul: the punch (and the AttendanceDay date/time) must use the local wall-clock.
        var localClock = new FixedLocalClock(new DateTime(2026, 9, 21, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 21, 9, 0, 0));
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34EF56", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", null, null, null));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        var handler = new QrCheckInHandler(_tenant, _currentUser, _locations, _days, _schedules, _shiftSettings, _selfCheckIns, localClock);

        var result = await handler.Handle(new QrCheckInCommand("AB12CD34EF56", PunchDirection.In), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Time.ShouldBe(new TimeOnly(9, 0));
        _days.Received(1).Add(Arg.Is<AttendanceDay>(d => d.CheckIn == new TimeOnly(9, 0)));
        _selfCheckIns.Received(1).Add(Arg.Is<SelfCheckIn>(c => c.TimestampUtc == new DateTime(2026, 9, 21, 6, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task QrCheckIn_MorningOutAfterEveningIn_ClosesYesterdaysNightShift()
    {
        var localClock = new FixedLocalClock(new DateTime(2026, 9, 21, 6, 0, 0, DateTimeKind.Utc));
        var yesterday = Today.AddDays(-1);
        var night = AttendanceDay.Record(_tenantId, _employeeId, yesterday, new TimeOnly(22, 0), null, new TimeOnly(22, 0), new TimeOnly(6, 0), 480, 0, AttendanceDaySource.SelfService, overnight: true);
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34EF56", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "Fabrika", null, null, null));
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        _days.FindByEmployeeAndDateAsync(_employeeId, yesterday, Arg.Any<CancellationToken>()).Returns(night);
        _schedules.GetForDateAsync(_employeeId, yesterday, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(22, 0), new TimeOnly(6, 0), null, null, true));
        var handler = new QrCheckInHandler(_tenant, _currentUser, _locations, _days, _schedules, _shiftSettings, _selfCheckIns, localClock);

        var result = await handler.Handle(new QrCheckInCommand("AB12CD34EF56", PunchDirection.Out), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Date.ShouldBe(yesterday);
        night.CheckIn.ShouldBe(new TimeOnly(22, 0));
        night.CheckOut.ShouldBe(new TimeOnly(6, 0));
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task GpsCheckIn_LocationWithRadius_FarAway_NeverWritesAnything()
    {
        // Server-side enforcement: only the submitted coordinates are compared to the fence, never a client-side verdict.
        _locations.GetGeofenceAsync(_tenantId, _locationId, Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", 39.925533, 32.866287, 150));

        var result = await CreateGpsHandler().Handle(new GpsCheckInCommand(0, 0, _locationId, PunchDirection.In), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInOutsideGeofence);
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
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
        var handler = new GetMyCheckInStatusHandler(_tenant, _currentUser, _days, _selfCheckIns, Clock);

        var result = await handler.Handle(new GetMyCheckInStatusQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsCheckedIn.ShouldBeTrue();
    }

    [Fact]
    public async Task GetMyCheckInStatus_NoRecordToday_ReportsNotCheckedIn()
    {
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns((AttendanceDay?)null);
        var handler = new GetMyCheckInStatusHandler(_tenant, _currentUser, _days, _selfCheckIns, Clock);

        var result = await handler.Handle(new GetMyCheckInStatusQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsCheckedIn.ShouldBeFalse();
        result.Value.Breaks.ShouldNotBeNull();
        result.Value.Breaks.ShouldBeEmpty();
    }

    // --- Mola başı/sonu (mobil yeniden tasarım §6): stateful fakes so a whole day's punch sequence can be replayed. ---

    private void UseStatefulStores()
    {
        _days.FindByEmployeeAndDateAsync(_employeeId, Today, Arg.Any<CancellationToken>()).Returns(_ => _todayRecord);
        _days.When(d => d.Add(Arg.Any<AttendanceDay>())).Do(c => _todayRecord = c.Arg<AttendanceDay>());
        _selfCheckIns.When(s => s.Add(Arg.Any<SelfCheckIn>())).Do(c => _punches.Add(c.Arg<SelfCheckIn>()));
        _selfCheckIns.FindLatestBreakPunchAsync(_employeeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(c => _punches
                .Where(p => p.TimestampUtc >= c.ArgAt<DateTime>(1) && p.Direction is PunchDirection.BreakStart or PunchDirection.BreakEnd)
                .OrderByDescending(p => p.TimestampUtc)
                .FirstOrDefault());
        _selfCheckIns.ListBreakAndOutPunchesAsync(_employeeId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(c => (IReadOnlyList<SelfCheckIn>)_punches
                .Where(p => p.TimestampUtc >= c.ArgAt<DateTime>(1) && p.Direction is PunchDirection.BreakStart or PunchDirection.BreakEnd or PunchDirection.Out)
                .OrderBy(p => p.TimestampUtc)
                .ToList());
    }

    private static FixedLocalClock At(int hour, int minute) => new(new DateTime(2026, 9, 21, hour, minute, 0, DateTimeKind.Utc));

    private Task<Result<SelfCheckInResultDto>> GpsAt(PunchDirection direction, int hour, int minute) =>
        new GpsCheckInHandler(_tenant, _currentUser, _locations, _days, _schedules, _shiftSettings, _selfCheckIns, At(hour, minute))
            .Handle(new GpsCheckInCommand(39.9, 32.8, null, direction), TestContext.Current.CancellationToken);

    private Task<Result<MyCheckInStatusDto>> StatusAt(int hour, int minute) =>
        new GetMyCheckInStatusHandler(_tenant, _currentUser, _days, _selfCheckIns, At(hour, minute))
            .Handle(new GetMyCheckInStatusQuery(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task BreakStart_WithoutCheckIn_ReturnsNotCheckedIn()
    {
        UseStatefulStores();

        var result = await GpsAt(PunchDirection.BreakStart, 12, 0);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInBreakNotCheckedIn);
        _punches.ShouldBeEmpty();
        _days.DidNotReceive().Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task BreakStart_Twice_ReturnsAlreadyOpen()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();

        var result = await GpsAt(PunchDirection.BreakStart, 12, 5);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInBreakAlreadyOpen);
        _punches.Count(p => p.Direction == PunchDirection.BreakStart).ShouldBe(1);
    }

    [Fact]
    public async Task BreakEnd_WithoutOpenBreak_ReturnsNotOpen()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();

        var result = await GpsAt(PunchDirection.BreakEnd, 12, 40);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInBreakNotOpen);
    }

    [Fact]
    public async Task BreakStartThenEnd_AddsBreakMinutes_WithoutTouchingCheckInOut()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();

        var result = await GpsAt(PunchDirection.BreakEnd, 12, 40);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Direction.ShouldBe(PunchDirection.BreakEnd);
        result.Value.Time.ShouldBe(new TimeOnly(12, 40));
        _todayRecord.ShouldNotBeNull();
        _todayRecord.ActualBreakMinutes.ShouldBe(40);
        _todayRecord.CheckIn.ShouldBe(new TimeOnly(10, 0));
        _todayRecord.CheckOut.ShouldBeNull();

        var status = await StatusAt(12, 45);
        status.Value.IsCheckedIn.ShouldBeTrue();
        status.Value.OnBreak.ShouldBeFalse();
        status.Value.BreakStartedAt.ShouldBeNull();
        status.Value.BreakMinutesToday.ShouldBe(40);
    }

    [Fact]
    public async Task Out_DuringOpenBreak_ClosesBreakAtCheckOut()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();

        var result = await GpsAt(PunchDirection.Out, 12, 30);

        result.IsSuccess.ShouldBeTrue();
        _todayRecord.ShouldNotBeNull();
        _todayRecord.ActualBreakMinutes.ShouldBe(30);
        _todayRecord.CheckOut.ShouldBe(new TimeOnly(12, 30));

        var status = await StatusAt(12, 35);
        status.Value.IsCheckedIn.ShouldBeFalse();
        status.Value.OnBreak.ShouldBeFalse();
        status.Value.BreakMinutesToday.ShouldBe(30);
    }

    [Fact]
    public async Task GetMyCheckInStatus_DuringOpenBreak_ReportsBreakStart()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();

        var status = await StatusAt(12, 10);

        status.IsSuccess.ShouldBeTrue();
        status.Value.IsCheckedIn.ShouldBeTrue();
        status.Value.OnBreak.ShouldBeTrue();
        status.Value.BreakStartedAt.ShouldBe(new TimeOnly(12, 0));
        status.Value.BreakMinutesToday.ShouldBe(0);
    }

    [Fact]
    public async Task BreakStart_AfterCheckOut_ReturnsNotCheckedIn()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.Out, 17, 0)).IsSuccess.ShouldBeTrue();

        var result = await GpsAt(PunchDirection.BreakStart, 17, 10);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.SelfCheckInBreakNotCheckedIn);
    }

    [Fact]
    public async Task QrCheckIn_BreakStart_RecordsBreakPunchWithoutChangingDay()
    {
        UseStatefulStores();
        _locations.FindBySelfCheckInCodeAsync(_tenantId, "AB12CD34", Arg.Any<CancellationToken>())
            .Returns(new LocationGeofence(_locationId, "İstanbul HQ", null, null, null));
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        var handler = new QrCheckInHandler(_tenant, _currentUser, _locations, _days, _schedules, _shiftSettings, _selfCheckIns, At(12, 0));

        var result = await handler.Handle(new QrCheckInCommand("AB12CD34", PunchDirection.BreakStart), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AttendanceDayId.ShouldBe(_todayRecord!.Id);
        _punches.ShouldContain(p => p.Direction == PunchDirection.BreakStart && p.Method == SelfCheckInMethod.Qr);
        _todayRecord.CheckIn.ShouldBe(new TimeOnly(10, 0));
        _todayRecord.ActualBreakMinutes.ShouldBe(0);
    }

    // --- Günün molaları (status "breaks"): tiles rebuilt after an app restart from the day's break/out rows. ---

    [Fact]
    public async Task GetMyCheckInStatus_TwoClosedBreaks_ListsBothInOrder()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 9, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakEnd, 10, 15)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakEnd, 12, 45)).IsSuccess.ShouldBeTrue();

        var status = await StatusAt(13, 0);

        status.IsSuccess.ShouldBeTrue();
        status.Value.OnBreak.ShouldBeFalse();
        status.Value.BreakMinutesToday.ShouldBe(60);
        status.Value.Breaks.ShouldBe(
        [
            new MyBreakDto(new TimeOnly(10, 0), new TimeOnly(10, 15)),
            new MyBreakDto(new TimeOnly(12, 0), new TimeOnly(12, 45)),
        ]);
    }

    [Fact]
    public async Task GetMyCheckInStatus_OpenBreak_IsListedLastWithoutEnd()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakEnd, 12, 40)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 15, 0)).IsSuccess.ShouldBeTrue();

        var status = await StatusAt(15, 10);

        status.Value.OnBreak.ShouldBeTrue();
        status.Value.BreakStartedAt.ShouldBe(new TimeOnly(15, 0));
        status.Value.Breaks.ShouldBe(
        [
            new MyBreakDto(new TimeOnly(12, 0), new TimeOnly(12, 40)),
            new MyBreakDto(new TimeOnly(15, 0), null),
        ]);
    }

    [Fact]
    public async Task GetMyCheckInStatus_BreakClosedByCheckOut_EndsAtCheckOut()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.BreakStart, 12, 0)).IsSuccess.ShouldBeTrue();
        (await GpsAt(PunchDirection.Out, 12, 30)).IsSuccess.ShouldBeTrue();

        var status = await StatusAt(12, 35);

        status.Value.IsCheckedIn.ShouldBeFalse();
        status.Value.Breaks.ShouldBe([new MyBreakDto(new TimeOnly(12, 0), new TimeOnly(12, 30))]);
    }

    [Fact]
    public async Task GetMyCheckInStatus_CheckedInWithoutBreaks_ReturnsEmptyBreaks()
    {
        UseStatefulStores();
        (await GpsAt(PunchDirection.In, 10, 0)).IsSuccess.ShouldBeTrue();

        var status = await StatusAt(11, 0);

        status.Value.IsCheckedIn.ShouldBeTrue();
        status.Value.Breaks.ShouldNotBeNull();
        status.Value.Breaks.ShouldBeEmpty();
    }
}
