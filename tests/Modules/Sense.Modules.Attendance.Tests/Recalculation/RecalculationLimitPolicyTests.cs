using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Attendance.Domain.Shifts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Recalculation;

/// <summary>
/// Kayıt anı ↔ yeniden hesap tutarlılığı (I0 düzeltme turu 1): yeniden hesap günü kayıt anındaki kanal kuralıyla kurar - elle/Excel günü
/// kiracı limitleri olmadan (gece dakikası 0, limit bayrağı yok), diğer kanallar kiracı limitleriyle. Kural tek yerde (yazıcı), rebuilder
/// aynısını günün kaynağına göre uygular.
/// </summary>
public sealed class RecalculationLimitPolicyTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    // Kiracı UTC: yerel = UTC.
    private static readonly FixedLocalClock Clock = new(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly InMemoryPunchRepository _punches = new();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IPlannedDayResolver _planner = Substitute.For<IPlannedDayResolver>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IShiftSettingsRepository _settings = Substitute.For<IShiftSettingsRepository>();
    private readonly List<AttendanceDay> _stored = [];
    private readonly ShiftLimits _tenantLimits;
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public RecalculationLimitPolicyTests()
    {
        // Varsayılan kiracı ayarı: gece çalışması açık, 20:00-06:00. 12:00-23:00 çalışmanın 180 dakikası gece.
        var settings = ShiftSettings.CreateDefault(_tenantId);
        _settings.FindAsync(Arg.Any<CancellationToken>()).Returns(settings);
        _tenantLimits = new ShiftLimits(settings.MaxDailyMinutes, settings.NightWorkEnabled, settings.NightWorkStart, settings.NightWorkEnd, settings.NightWorkMaxMinutes);
        _planner.ResolveAsync(_employeeId, Day, Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new PlannedDay(Start: new TimeOnly(12, 0), End: new TimeOnly(21, 0), PlannedMinutes: 480, BreakMinutes: 60));
        _days.When(d => d.Add(Arg.Any<AttendanceDay>())).Do(call => _stored.Add(call.Arg<AttendanceDay>()));
        _days.FindByEmployeeAndDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_stored.FirstOrDefault(d => d.Date == call.ArgAt<DateOnly>(1))));
    }

    private PunchDayWriter Writer() => new(_tenantId, _days, _punches, _planner, _approvals, TimeZoneInfo.Utc, new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));

    private PunchDayRebuilder Rebuilder() => new(_days, _punches, _planner, _approvals, _settings, Clock);

    private static (int Night, bool DailyLimit, bool NightLimit, int Net, AttendanceAnomaly Anomalies) Snapshot(AttendanceDay d) =>
        (d.NightMinutes, d.DailyLimitExceeded, d.NightLimitExceeded, d.NetWorkedMinutes, d.Anomalies);

    private void Punch(PunchDirection direction, int hour, PunchSource source) =>
        _punches.Add(Sense.Modules.Attendance.Domain.Punches.Punch.Create(
            _tenantId, _employeeId, Day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc), Day, direction, source, null, null, null, null, null,
            source == PunchSource.Manual, null, null, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)).Value);

    [Fact]
    public async Task Recalc_of_manual_day_keeps_limitless_and_night_minutes()
    {
        // Elle kayıt yolu (RecordAttendanceHandler → ReplaceDayAsync): kiracı limitleri uygulanmaz, gece dakikası 0.
        var recorded = await Writer().ReplaceDayAsync(
            _employeeId, Day, new TimeOnly(12, 0), new TimeOnly(23, 0), AttendanceDaySource.Manual, null, "test", limits: null, ct);
        recorded.Value.NightMinutes.ShouldBe(0);
        var before = Snapshot(recorded.Value);

        var rebuilt = await Rebuilder().RebuildAsync(_tenantId, _employeeId, Day, ct);

        Snapshot(rebuilt.ShouldNotBeNull()).ShouldBe(before, "yeniden hesap elle günün gece dakikasını / limit bayraklarını değiştirmemeli");
    }

    [Fact]
    public async Task Recalc_of_device_day_applies_tenant_limits()
    {
        Punch(PunchDirection.In, 12, PunchSource.Device);
        Punch(PunchDirection.Out, 23, PunchSource.Device);
        var recorded = await Writer().RebuildAsync(_employeeId, Day, AttendanceDaySource.Device, _tenantLimits, forceIncalculable: false, ct);
        recorded.Value.NightMinutes.ShouldBe(180);
        var before = Snapshot(recorded.Value);

        var rebuilt = await Rebuilder().RebuildAsync(_tenantId, _employeeId, Day, ct);

        Snapshot(rebuilt.ShouldNotBeNull()).ShouldBe(before);
        rebuilt.NightMinutes.ShouldBe(180);
    }

    [Theory]
    [InlineData(AttendanceDaySource.Manual, PunchSource.Manual, 0)]
    [InlineData(AttendanceDaySource.Import, PunchSource.Import, 0)]
    [InlineData(AttendanceDaySource.Device, PunchSource.Device, 180)]
    [InlineData(AttendanceDaySource.SelfService, PunchSource.SelfService, 180)]
    [InlineData(AttendanceDaySource.Request, PunchSource.Request, 180)]
    [InlineData(AttendanceDaySource.Terminal, PunchSource.Terminal, 180)]
    public async Task Writer_and_rebuilder_use_same_policy_for_every_source(AttendanceDaySource daySource, PunchSource punchSource, int nightMinutes)
    {
        // Kanal kiracı limitlerini verse de gün kaynağının kuralı geçerlidir; rebuilder aynı kuralı kaynağa göre uygular.
        Punch(PunchDirection.In, 12, punchSource);
        Punch(PunchDirection.Out, 23, punchSource);
        var recorded = await Writer().RebuildAsync(_employeeId, Day, daySource, _tenantLimits, forceIncalculable: false, ct);
        var before = Snapshot(recorded.Value);

        var rebuilt = await Rebuilder().RebuildAsync(_tenantId, _employeeId, Day, ct);

        before.Night.ShouldBe(nightMinutes);
        Snapshot(rebuilt.ShouldNotBeNull()).ShouldBe(before);
    }
}
