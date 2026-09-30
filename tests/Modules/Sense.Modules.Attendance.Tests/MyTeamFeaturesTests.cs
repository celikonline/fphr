using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MyTeam;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>FG-007 "Ekibim": attendance/timesheet/me/team(/month) and attendance/me/team/today scoped to the caller's direct reports.</summary>
public sealed class MyTeamFeaturesTests
{
    // 2026-09-23 is a Wednesday.
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly FixedLocalClock Clock = new(new DateTime(2026, 9, 23, 7, 0, 0, DateTimeKind.Utc));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _inside = Guid.NewGuid();
    private readonly Guid _left = Guid.NewGuid();
    private readonly Guid _onLeave = Guid.NewGuid();
    private readonly Guid _absent = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IEmployeeLookup _lookup = Substitute.For<IEmployeeLookup>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IApprovedLeaveDateLookup _leaveDates = Substitute.For<IApprovedLeaveDateLookup>();

    public MyTeamFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_managerId);
        _lookup.GetDirectReportIdsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new List<Guid>());
        _schedules.IsWorkingDayAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(true);
        _leaveDates.GetApprovedLeaveDatesAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovedLeaveDay>());
    }

    private GetMyTeamTodayHandler TodayHandler() => new(_tenant, _currentUser, _lookup, _employees, _days, new InMemoryPunchRepository(), Substitute.For<ITerminalRepository>(), Clock, _leaveDates);

    private GetMyTeamWeekHandler WeekHandler() =>
        new(_tenant, _currentUser, _lookup, _employees, _days, _schedules, new NonWorkingDayResolver(_leaveDates));

    private GetMyTeamMonthHandler MonthHandler() =>
        new(_tenant, _currentUser, _lookup, _employees, _days, _schedules, new NonWorkingDayResolver(_leaveDates));

    private void Reports(params Guid[] ids) =>
        _lookup.GetDirectReportIdsAsync(_tenantId, _managerId, Arg.Any<CancellationToken>()).Returns(ids.ToList());

    private void Names(params (Guid Id, string First)[] people) =>
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(people.Select(p => new EmployeeContact(p.Id, p.First, "Test", null, null, null, "Active")).ToList());

    private AttendanceDay Day(Guid employeeId, DateOnly date, TimeOnly? checkIn, TimeOnly? checkOut) =>
        AttendanceDay.Record(_tenantId, employeeId, date, checkIn, checkOut, new TimeOnly(9, 0), new TimeOnly(18, 0), 540, 0, AttendanceDaySource.SelfService);

    [Theory]
    [InlineData(true, "09:00", null, false, MyTeamTodayStatus.CheckedIn)]
    [InlineData(true, "09:00", null, true, MyTeamTodayStatus.CheckedIn)]
    [InlineData(false, "09:00", "18:00", false, MyTeamTodayStatus.CheckedOut)]
    [InlineData(false, "09:00", "18:00", true, MyTeamTodayStatus.CheckedOut)]
    [InlineData(false, null, null, true, MyTeamTodayStatus.OnLeave)]
    [InlineData(false, null, null, false, MyTeamTodayStatus.NotCheckedIn)]
    public void Map_PunchWinsOverLeave(bool inside, string? checkIn, string? checkOut, bool onLeave, MyTeamTodayStatus expected)
    {
        var row = new LiveAttendanceStatusDto(
            Guid.NewGuid(), "x", checkIn is null ? null : TimeOnly.Parse(checkIn), checkOut is null ? null : TimeOnly.Parse(checkOut), inside, null);

        MyTeamTodayStatusMapper.Map(row, onLeave).ShouldBe(expected);
    }

    [Fact]
    public async Task Today_ReportsEachDirectReportsStatus_OrderedByName_WithoutTheLeaveType()
    {
        Reports(_absent, _inside, _left, _onLeave);
        Names((_inside, "Ayşe"), (_left, "Burak"), (_onLeave, "Cem"), (_absent, "Deniz"));
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Today.AddDays(-1), Today, Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { Day(_inside, Today, new TimeOnly(8, 55), null), Day(_left, Today, new TimeOnly(7, 0), new TimeOnly(15, 0)) });
        _leaveDates.GetApprovedLeaveDatesAsync(_tenantId, Today, Today, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovedLeaveDay> { new(_onLeave, Today) });

        var result = await TodayHandler().Handle(new GetMyTeamTodayQuery(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(r => (r.EmployeeId, r.Status)).ShouldBe(
        [
            (_inside, MyTeamTodayStatus.CheckedIn),
            (_left, MyTeamTodayStatus.CheckedOut),
            (_onLeave, MyTeamTodayStatus.OnLeave),
            (_absent, MyTeamTodayStatus.NotCheckedIn),
        ]);
        result.Value[0].CheckIn.ShouldBe(new TimeOnly(8, 55));
        result.Value[1].CheckOut.ShouldBe(new TimeOnly(15, 0));
    }

    [Fact]
    public async Task Today_WithoutLeaveModule_ReportsNobodyOnLeave()
    {
        Reports(_absent);
        Names((_absent, "Deniz"));
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay>());

        var result = await new GetMyTeamTodayHandler(_tenant, _currentUser, _lookup, _employees, _days, new InMemoryPunchRepository(), Substitute.For<ITerminalRepository>(), Clock)
            .Handle(new GetMyTeamTodayQuery(), TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem().Status.ShouldBe(MyTeamTodayStatus.NotCheckedIn);
    }

    [Fact]
    public async Task Week_ReadsOnlyTheDirectReports()
    {
        Reports(_inside);
        Names((_inside, "Ayşe"));
        var monday = new DateOnly(2026, 9, 21);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), monday, monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { Day(_inside, monday, new TimeOnly(9, 0), new TimeOnly(18, 0)) });

        var result = await WeekHandler().Handle(new GetMyTeamWeekQuery(Today), TestContext.Current.CancellationToken);

        var row = result.Value.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(_inside);
        row.Days.Count.ShouldBe(7);
        row.Days[0].Date.ShouldBe(monday);
        row.Days[0].AttendanceDayId.ShouldNotBeNull();
        await _days.Received(1).ListForEmployeesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { _inside })), monday, monday.AddDays(6), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Month_CoversTheWholeCalendarMonth()
    {
        Reports(_inside);
        Names((_inside, "Ayşe"));
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay>());

        var result = await MonthHandler().Handle(new GetMyTeamMonthQuery(Today), TestContext.Current.CancellationToken);

        var row = result.Value.ShouldHaveSingleItem();
        row.Days.Count.ShouldBe(30);
        row.Days[0].Date.ShouldBe(new DateOnly(2026, 9, 1));
        row.Days[^1].Date.ShouldBe(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public async Task ManagerWithoutReports_GetsEmptyLists_AndNoAttendanceIsRead()
    {
        Reports();

        (await TodayHandler().Handle(new GetMyTeamTodayQuery(), TestContext.Current.CancellationToken)).Value.ShouldBeEmpty();
        (await WeekHandler().Handle(new GetMyTeamWeekQuery(Today), TestContext.Current.CancellationToken)).Value.ShouldBeEmpty();
        (await MonthHandler().Handle(new GetMyTeamMonthQuery(Today), TestContext.Current.CancellationToken)).Value.ShouldBeEmpty();

        await _days.DidNotReceiveWithAnyArgs().ListForEmployeesAsync(default!, default, default, default);
    }

    [Fact]
    public async Task CallerWithoutLinkedEmployee_GetsEmptyLists_WithoutLookingUpAnyone()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        (await TodayHandler().Handle(new GetMyTeamTodayQuery(), TestContext.Current.CancellationToken)).Value.ShouldBeEmpty();
        (await WeekHandler().Handle(new GetMyTeamWeekQuery(Today), TestContext.Current.CancellationToken)).Value.ShouldBeEmpty();

        await _lookup.DidNotReceiveWithAnyArgs().GetDirectReportIdsAsync(default, default, default);
        await _days.DidNotReceiveWithAnyArgs().ListForEmployeesAsync(default!, default, default, default);
    }
}
