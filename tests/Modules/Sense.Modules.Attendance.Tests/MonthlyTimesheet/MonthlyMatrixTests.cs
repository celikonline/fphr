using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MonthlyTimesheet;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.MonthlyTimesheet;

/// <summary>GET attendance/monthly-matrix (Z3.4, B11): calisan x gun durum kodu matrisi.</summary>
public sealed class MonthlyMatrixTests
{
    // Eylul 2026: 1'i Sali. Yerel "bugun" 30 Eylul (tum ay gecmis).
    private static readonly DateOnly Period = new(2026, 9, 15);
    private static readonly DateOnly Month = new(2026, 9, 1);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeB = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IShiftAssignmentRepository _assignments = Substitute.For<IShiftAssignmentRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IApprovedLeaveDateLookup _leaveDates = Substitute.For<IApprovedLeaveDateLookup>();
    private readonly IHolidayCalendarLookup _holidays = Substitute.For<IHolidayCalendarLookup>();
    private readonly ILeaveOnDateReader _leaveOnDate = Substitute.For<ILeaveOnDateReader>();

    public MonthlyMatrixTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => ((IReadOnlyCollection<Guid>)call[1]).Select(id => new EmployeeContact(id, id == _employeeA ? "Ayse" : "Can", "Yilmaz", null, null, null, "Active")).ToList());
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());
        _assignments.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(new List<ShiftAssignment>());
        _schedules.GetWorkingDaysAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => Enumerable.Range(0, ((DateOnly)call[2]).DayNumber - ((DateOnly)call[1]).DayNumber + 1)
                .Select(i => ((DateOnly)call[1]).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList());
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<MonthlyTimesheetApproval>());
        _leaveDates.GetApprovedLeaveDatesAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovedLeaveDay>());
        _leaveOnDate.ListAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(new List<EmployeeLeaveOnDate>());
    }

    private GetMonthlyMatrixHandler CreateHandler() => new(
        _tenant, _dataScope, _employees, _days, _assignments, _schedules, _approvals, new NonWorkingDayResolver(_leaveDates, _holidays),
        new FixedLocalClock(new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc)), _leaveOnDate);

    private async Task<MonthlyMatrixDto> RunAsync(int page = 1, int pageSize = MonthlyMatrixLimits.DefaultPageSize)
    {
        var result = await CreateHandler().Handle(new GetMonthlyMatrixQuery(Period, null, page, pageSize), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static string CodeOf(MonthlyMatrixDto matrix, Guid employeeId, int day) =>
        matrix.Rows.Single(r => r.EmployeeId == employeeId).Cells.Single(c => c.Date == new DateOnly(2026, 9, day)).Code;

    private AttendanceDay Record(int day, TimeOnly? checkIn, TimeOnly? checkOut, Guid? employeeId = null) => AttendanceDay.Record(
        _tenantId, employeeId ?? _employeeA, new DateOnly(2026, 9, day), checkIn, checkOut,
        new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 60, AttendanceDaySource.Manual);

    private void SeedDays(params AttendanceDay[] days) =>
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(days.ToList());

    [Fact]
    public async Task Every_day_of_month_is_returned_for_each_employee()
    {
        var matrix = await RunAsync();

        var row = matrix.Rows.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe("Ayse Yilmaz");
        row.Cells.Count.ShouldBe(30);
        row.Cells.Select(c => c.Date).ShouldBe(Enumerable.Range(1, 30).Select(d => new DateOnly(2026, 9, d)));
    }

    [Fact]
    public async Task Full_code_for_planned_worked_day()
    {
        SeedDays(Record(1, new TimeOnly(9, 0), new TimeOnly(18, 0)));

        CodeOf(await RunAsync(), _employeeA, 1).ShouldBe("full");
    }

    [Fact]
    public async Task Overtime_code_for_overwork_day()
    {
        SeedDays(Record(1, new TimeOnly(9, 0), new TimeOnly(20, 0)));

        CodeOf(await RunAsync(), _employeeA, 1).ShouldBe("overtime");
    }

    [Fact]
    public async Task Missing_code_for_missing_checkout()
    {
        SeedDays(Record(1, new TimeOnly(9, 0), null));

        CodeOf(await RunAsync(), _employeeA, 1).ShouldBe("missing");
    }

    [Fact]
    public async Task Absent_code_for_past_working_day_without_record()
    {
        // 2 Eylul Carsamba, kayit yok, izin/tatil yok.
        CodeOf(await RunAsync(), _employeeA, 2).ShouldBe("absent");
    }

    [Fact]
    public async Task Incalculable_code_for_flagged_day()
    {
        var day = Record(1, new TimeOnly(9, 0), null);
        typeof(AttendanceDay).GetProperty(nameof(AttendanceDay.Anomalies))!.SetValue(day, AttendanceAnomaly.Incalculable);
        SeedDays(day);

        CodeOf(await RunAsync(), _employeeA, 1).ShouldBe("incalculable");
    }

    [Fact]
    public async Task WeeklyRest_code_for_non_working_schedule_day()
    {
        // 5 Eylul Cumartesi.
        CodeOf(await RunAsync(), _employeeA, 5).ShouldBe("weeklyRest");
    }

    [Theory]
    [InlineData(ShiftDayType.WeeklyRest, "weeklyRest")]
    [InlineData(ShiftDayType.RestDay, "restDay")]
    [InlineData(ShiftDayType.PublicHoliday, "publicHoliday")]
    public async Task Planned_day_type_maps_to_code(ShiftDayType dayType, string expected)
    {
        var assignment = ShiftAssignment.Create(_tenantId, _employeeA, new DateOnly(2026, 9, 2), new TimeOnly(9, 0), new TimeOnly(18, 0), 60, false, null).Value;
        assignment.SetPlan(null, dayType);
        _assignments.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment> { assignment });

        CodeOf(await RunAsync(), _employeeA, 2).ShouldBe(expected);
    }

    [Fact]
    public async Task PublicHoliday_code_from_holiday_calendar()
    {
        var calendarId = Guid.NewGuid();
        _holidays.ResolveCalendarAsync(_tenantId, null, null, Arg.Any<CancellationToken>()).Returns(new HolidayCalendarRef(calendarId, "TR"));
        _holidays.GetHolidaysAsync(_tenantId, calendarId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<UpcomingHoliday> { new(new DateOnly(2026, 9, 2), "Tatil") });

        CodeOf(await RunAsync(), _employeeA, 2).ShouldBe("publicHoliday");
    }

    [Theory]
    [InlineData("Annual", true, "paidLeave")]
    [InlineData("Unpaid", false, "unpaidLeave")]
    [InlineData("Sick", true, "otherLeave")]
    public async Task Leave_code_from_leave_type(string leaveTypeCode, bool isPaid, string expected)
    {
        _leaveDates.GetApprovedLeaveDatesAsync(_tenantId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovedLeaveDay> { new(_employeeA, new DateOnly(2026, 9, 2)) });
        _leaveOnDate.ListAsync(_tenantId, new DateOnly(2026, 9, 2), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeLeaveOnDate> { new(_employeeA, leaveTypeCode, isPaid) });

        CodeOf(await RunAsync(), _employeeA, 2).ShouldBe(expected);
    }

    [Fact]
    public async Task None_code_for_future_working_day()
    {
        var handler = new GetMonthlyMatrixHandler(
            _tenant, _dataScope, _employees, _days, _assignments, _schedules, _approvals, new NonWorkingDayResolver(_leaveDates, _holidays),
            new FixedLocalClock(new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc)), _leaveOnDate);

        var result = await handler.Handle(new GetMonthlyMatrixQuery(Period, null, 1, 50), TestContext.Current.CancellationToken);

        CodeOf(result.Value, _employeeA, 11).ShouldBe("none");
        CodeOf(result.Value, _employeeA, 9).ShouldBe("absent");
    }

    [Fact]
    public async Task Locked_period_flag_matches_approval()
    {
        (await RunAsync()).IsLocked.ShouldBeFalse();

        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Month, Arg.Any<CancellationToken>())
            .Returns(new List<MonthlyTimesheetApproval> { MonthlyTimesheetApproval.Approve(_tenantId, _employeeA, Month, DateTime.UtcNow, null) });

        (await RunAsync()).IsLocked.ShouldBeTrue();
    }

    [Fact]
    public async Task Scope_applied()
    {
        // Kapsam yalniz A; B dondurulmemeli, kapsam dislari sorgulanmamali.
        var matrix = await RunAsync();

        matrix.Rows.Select(r => r.EmployeeId).ShouldBe([_employeeA]);
        await _days.Received().ListForEmployeesAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(_employeeA)), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_visible_employees_returns_empty_unlocked()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyView.Key, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var matrix = await RunAsync();

        matrix.Rows.ShouldBeEmpty();
        matrix.IsLocked.ShouldBeFalse();
    }

    [Fact]
    public async Task Paging_is_stable_and_capped()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA, _employeeB });

        var first = await RunAsync(1, 1);
        var second = await RunAsync(2, 1);

        first.Rows.ShouldHaveSingleItem().EmployeeName.ShouldBe("Ayse Yilmaz");
        second.Rows.ShouldHaveSingleItem().EmployeeName.ShouldBe("Can Yilmaz");

        var capped = await CreateHandler().Handle(new GetMonthlyMatrixQuery(Period, null, 1, 100000), TestContext.Current.CancellationToken);
        capped.IsSuccess.ShouldBeTrue();
    }
}
