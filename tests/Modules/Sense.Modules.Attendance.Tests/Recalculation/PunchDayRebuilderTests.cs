using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Punches;
using Sense.Modules.Attendance.Application.Recalculation;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Recalculation;

/// <summary>
/// <see cref="PunchDayRebuilder"/> (zaman-devam I0): yeniden hesap günü okutmalardan, kanalların K2 planı ve Incalculable kurallarıyla
/// kurar - gece vardiyası çakışması (<see cref="WorkDateResolver"/>) yeniden hesapta da yakalanır; okutması ve kaydı olmayan gün kurulmaz;
/// kilitli ay (K6) atlanır.
/// </summary>
public sealed class PunchDayRebuilderTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1);
    private static readonly DateOnly Sep2 = new(2026, 9, 2);

    // Kiracı UTC: yerel = UTC.
    private static readonly FixedLocalClock Clock = new(new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly InMemoryPunchRepository _punches = new();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IPlannedDayResolver _planner = Substitute.For<IPlannedDayResolver>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly List<AttendanceDay> _stored = [];
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public PunchDayRebuilderTests()
    {
        _days.When(d => d.Add(Arg.Any<AttendanceDay>())).Do(call => _stored.Add(call.Arg<AttendanceDay>()));
        _days.FindByEmployeeAndDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_stored.FirstOrDefault(d => d.Date == call.ArgAt<DateOnly>(1))));
    }

    private PunchDayRebuilder Rebuilder() =>
        new(_days, _punches, _planner, _approvals, Substitute.For<IShiftSettingsRepository>(), Clock);

    private void Plan(DateOnly date, string start, string end) =>
        _planner.ResolveAsync(_employeeId, date, Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>())
            .Returns(new PlannedDay(Start: TimeOnly.Parse(start), End: TimeOnly.Parse(end), PlannedMinutes: 450, BreakMinutes: 30));

    private void Punch(DateOnly workDate, PunchDirection direction, DateTime utc) =>
        _punches.Add(Sense.Modules.Attendance.Domain.Punches.Punch.Create(
            _tenantId, _employeeId, utc, workDate, direction, PunchSource.Terminal, null, null, null, null, null, false, null, null,
            new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc)).Value);

    [Fact]
    public async Task Night_shift_overlap_is_caught_again_on_recalculation()
    {
        // 02:00, 1 Eylül 22:00 gece vardiyasından 4 sa sonra, 2 Eylül 06:00 erken vardiyasından 4 sa önce - eşit uzaklık.
        Plan(Sep1, "22:00", "06:00");
        Plan(Sep2, "06:00", "14:00");
        Punch(Sep1, PunchDirection.In, new DateTime(2026, 9, 1, 22, 0, 0, DateTimeKind.Utc));
        Punch(Sep1, PunchDirection.Out, new DateTime(2026, 9, 2, 2, 0, 0, DateTimeKind.Utc));

        var day = await Rebuilder().RebuildAsync(_tenantId, _employeeId, Sep1, ct);

        day.ShouldNotBeNull().Anomalies.ShouldBe(AttendanceAnomaly.Incalculable);
        day.WorkedMinutes.ShouldBe(0);
    }

    [Fact]
    public async Task Same_punches_without_the_competing_shift_rebuild_normally()
    {
        Plan(Sep1, "22:00", "06:00");
        Punch(Sep1, PunchDirection.In, new DateTime(2026, 9, 1, 22, 0, 0, DateTimeKind.Utc));
        Punch(Sep1, PunchDirection.Out, new DateTime(2026, 9, 2, 2, 0, 0, DateTimeKind.Utc));

        var day = await Rebuilder().RebuildAsync(_tenantId, _employeeId, Sep1, ct);

        day.ShouldNotBeNull().Anomalies.HasFlag(AttendanceAnomaly.Incalculable).ShouldBeFalse();
        (day.CheckIn, day.CheckOut).ShouldBe((new TimeOnly(22, 0), new TimeOnly(2, 0)));
    }

    [Fact]
    public async Task Day_without_record_and_without_punches_is_not_created()
    {
        Plan(Sep1, "09:00", "18:00");

        (await Rebuilder().RebuildAsync(_tenantId, _employeeId, Sep1, ct)).ShouldBeNull();

        _stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task Punches_without_a_day_record_create_the_day()
    {
        Plan(Sep1, "09:00", "18:00");
        Punch(Sep1, PunchDirection.In, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));
        Punch(Sep1, PunchDirection.Out, new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc));

        var day = await Rebuilder().RebuildAsync(_tenantId, _employeeId, Sep1, ct);

        day.ShouldNotBeNull().Source.ShouldBe(AttendanceDaySource.Terminal);
        _stored.ShouldHaveSingleItem().ShouldBeSameAs(day);
    }

    [Fact]
    public async Task Locked_month_is_not_rebuilt()
    {
        Plan(Sep1, "09:00", "18:00");
        Punch(Sep1, PunchDirection.In, new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc));
        _approvals.FindAsync(_employeeId, Sep1, Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, Sep1, DateTime.UtcNow, null));

        (await Rebuilder().RebuildAsync(_tenantId, _employeeId, Sep1, ct)).ShouldBeNull();

        _stored.ShouldBeEmpty();
    }
}
