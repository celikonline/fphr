using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Daily;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Daily;

/// <summary>Spec 5.4 status table: each DailyStatus list holds exactly the employees its rule selects.</summary>
public sealed class DailyAttendanceQueryTests
{
    private static readonly DateOnly Day = new(2026, 9, 21);
    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeOnly Six = new(18, 0);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IPunchRepository _punches = Substitute.For<IPunchRepository>();
    private readonly ILeaveOnDateReader _leave = Substitute.For<ILeaveOnDateReader>();
    private readonly IPlannedDayResolver _planner = Substitute.For<IPlannedDayResolver>();
    private readonly Dictionary<string, Guid> _ids = [];
    private readonly List<AttendanceDay> _records = [];
    private readonly List<EmployeeLeaveOnDate> _leaves = [];

    public DailyAttendanceQueryTests()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyViewKey, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _employees.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(_ => _ids.Values.ToList());
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => _ids.Where(kv => call.Arg<IReadOnlyCollection<Guid>>().Contains(kv.Value))
                .Select(kv => new EmployeeContact(kv.Value, kv.Key, "", null, null, null, "Active")).ToList());
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Day, Day, Arg.Any<CancellationToken>()).Returns(_ => _records.ToList());
        _leave.ListAsync(_tenantId, Day, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(_ => _leaves.ToList());
        _punches.ListForRangeAsync(_tenantId, Day, Day, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Sense.Modules.Attendance.Domain.Punches.Punch>());
    }

    private DailyAttendanceQuery Create() => new(_dataScope, _employees, _orgUnits, _days, _punches, _leave, _planner);

    private Guid Employee(string name) => _ids[name] = Guid.NewGuid();

    /// <summary>Record with the given schedule/actuals; the domain derives the anomaly flags.</summary>
    private void Record(string name, TimeOnly? checkIn, TimeOnly? checkOut, TimeOnly? plannedStart = null, TimeOnly? plannedEnd = null, int planned = 540) =>
        _records.Add(AttendanceDay.Record(_tenantId, _ids[name], Day, checkIn, checkOut, plannedStart ?? Nine, plannedEnd ?? Six, planned, 0, AttendanceDaySource.Device));

    private async Task<string[]> NamesAsync(DailyStatus status)
    {
        var page = await Create().ListAsync(_tenantId, Day, status, null, false, 1, 100, TestContext.Current.CancellationToken);
        return page.Items.Select(r => r.EmployeeName).Order().ToArray();
    }

    private void SeedAllStatuses()
    {
        Employee("Arrived"); Record("Arrived", Nine, Six);
        Employee("Late"); Record("Late", new TimeOnly(10, 0), Six);
        Employee("Early"); Record("Early", Nine, new TimeOnly(16, 0));
        Employee("Absent"); Record("Absent", null, null);
        Employee("LeaveAbsent"); Record("LeaveAbsent", null, null);
        _leaves.Add(new EmployeeLeaveOnDate(_ids["LeaveAbsent"], "ANNUAL", true));
        Employee("Overtime"); Record("Overtime", Nine, new TimeOnly(20, 0));
        Employee("NoOut"); Record("NoOut", Nine, null);
        Employee("Idle");
    }

    [Fact]
    public async Task Late_list_holds_late_arrivals() { SeedAllStatuses(); (await NamesAsync(DailyStatus.Late)).ShouldBe(["Late"]); }

    [Fact]
    public async Task EarlyLeave_list_holds_early_leavers() { SeedAllStatuses(); (await NamesAsync(DailyStatus.EarlyLeave)).ShouldContain("Early"); }

    [Fact]
    public async Task Absent_excludes_employees_with_approved_leave()
    {
        SeedAllStatuses();
        (await NamesAsync(DailyStatus.Absent)).ShouldBe(["Absent"]);
    }

    [Fact]
    public async Task OnLeave_uses_leave_reader_and_sets_leave_type()
    {
        SeedAllStatuses();
        var page = await Create().ListAsync(_tenantId, Day, DailyStatus.OnLeave, null, false, 1, 100, TestContext.Current.CancellationToken);
        var row = page.Items.ShouldHaveSingleItem();
        row.EmployeeName.ShouldBe("LeaveAbsent");
        row.LeaveType.ShouldBe("ANNUAL");
    }

    [Fact]
    public async Task Overtime_list_holds_employees_with_overtime()
    {
        SeedAllStatuses();
        (await NamesAsync(DailyStatus.Overtime)).ShouldContain("Overtime");
        (await NamesAsync(DailyStatus.Overtime)).ShouldNotContain("Arrived");
    }

    [Fact]
    public async Task Arrived_list_holds_employees_with_a_check_in()
    {
        SeedAllStatuses();
        var arrived = await NamesAsync(DailyStatus.Arrived);
        arrived.ShouldContain("Arrived");
        arrived.ShouldContain("Late");
        arrived.ShouldNotContain("Absent");
        arrived.ShouldNotContain("Idle");
    }

    [Fact]
    public async Task Incalculable_days_appear_only_in_the_incalculable_list()
    {
        Employee("Odd");
        var day = AttendanceDay.Record(_tenantId, _ids["Odd"], Day, Nine, null, Nine, Six, 540, 0, AttendanceDaySource.Device);
        typeof(AttendanceDay).GetProperty(nameof(AttendanceDay.Anomalies))!.SetValue(day, AttendanceAnomaly.Incalculable);
        _records.Add(day);

        (await NamesAsync(DailyStatus.Incalculable)).ShouldBe(["Odd"]);
        foreach (var status in Enum.GetValues<DailyStatus>().Where(s => s is not (DailyStatus.Incalculable or DailyStatus.OnLeave)))
        {
            (await NamesAsync(status)).ShouldBeEmpty($"{status} must not list an incalculable day");
        }
    }

    [Fact]
    public async Task Every_list_matches_day_anomaly_flags()
    {
        SeedAllStatuses();
        var expected = new Dictionary<DailyStatus, Func<AttendanceDay, bool>>
        {
            [DailyStatus.Late] = d => d.Anomalies.HasFlag(AttendanceAnomaly.LateArrival),
            [DailyStatus.EarlyLeave] = d => d.Anomalies.HasFlag(AttendanceAnomaly.EarlyLeave),
            [DailyStatus.Undertime] = d => d.Anomalies.HasFlag(AttendanceAnomaly.Underwork),
            [DailyStatus.Overtime] = d => d.Anomalies.HasFlag(AttendanceAnomaly.Overwork) || d.OvertimeMinutes > 0,
            [DailyStatus.Arrived] = d => d.CheckIn is not null,
        };

        foreach (var (status, rule) in expected)
        {
            var fromDays = _records.Where(d => !d.Anomalies.HasFlag(AttendanceAnomaly.Incalculable) && rule(d))
                .Select(d => _ids.First(kv => kv.Value == d.EmployeeId).Key).Order().ToArray();
            (await NamesAsync(status)).ShouldBe(fromDays, status.ToString());
        }
    }

    [Fact]
    public async Task Counts_equal_list_totals()
    {
        SeedAllStatuses();
        var counts = await Create().CountsAsync(_tenantId, Day, null, false, TestContext.Current.CancellationToken);

        counts.Keys.ShouldBe(Enum.GetValues<DailyStatus>(), ignoreOrder: true);
        foreach (var status in Enum.GetValues<DailyStatus>())
        {
            var page = await Create().ListAsync(_tenantId, Day, status, null, false, 1, 1, TestContext.Current.CancellationToken);
            counts[status].ShouldBe((int)page.TotalCount, status.ToString());
        }
    }

    [Fact]
    public async Task Respects_data_scope()
    {
        SeedAllStatuses();
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _ids["Late"] });

        (await NamesAsync(DailyStatus.Arrived)).ShouldBe(["Late"]);
        (await NamesAsync(DailyStatus.Absent)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_scope_returns_empty_page()
    {
        SeedAllStatuses();
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.DailyViewKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var page = await Create().ListAsync(_tenantId, Day, DailyStatus.Arrived, null, false, 1, 10, TestContext.Current.CancellationToken);
        page.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Org_unit_filter_expands_subunits_when_requested()
    {
        SeedAllStatuses();
        var unit = Guid.NewGuid();
        var child = Guid.NewGuid();
        _orgUnits.GetSelfAndDescendantIdsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([unit, child]);
        _employees.ListActiveEmployeeIdsInOrgUnitsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([_ids["Late"]]);

        var page = await Create().ListAsync(_tenantId, Day, DailyStatus.Arrived, unit, true, 1, 10, TestContext.Current.CancellationToken);

        page.Items.Select(r => r.EmployeeName).ShouldBe(["Late"]);
        await _employees.Received().ListActiveEmployeeIdsInOrgUnitsAsync(
            _tenantId, Arg.Is<IReadOnlyCollection<Guid>>(c => c.Contains(child)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Daily_row_carries_incalculable_reason_without_n_plus_1()
    {
        // Two incalculable employees with punches but no plan at all, plus one healthy employee.
        foreach (var name in new[] { "BadA", "BadB" })
        {
            var id = Employee(name);
            var day = AttendanceDay.Record(_tenantId, id, Day, Nine, Six, Nine, Six, 540, 0, AttendanceDaySource.Device);
            day.MarkIncalculable();
            _records.Add(day);
        }

        Employee("Fine"); Record("Fine", Nine, Six);
        var punchList = _ids.Where(kv => kv.Key.StartsWith("Bad", StringComparison.Ordinal)).SelectMany(kv => new[]
        {
            Sense.Modules.Attendance.Domain.Punches.Punch.Create(
                _tenantId, kv.Value, new DateTime(2026, 9, 21, 6, 0, 0, DateTimeKind.Utc), Day, Sense.Modules.Attendance.Domain.SelfCheckIns.PunchDirection.In,
                Sense.Modules.Attendance.Domain.Punches.PunchSource.Device, null, null, null, null, null, false, null, null,
                new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)).Value,
            Sense.Modules.Attendance.Domain.Punches.Punch.Create(
                _tenantId, kv.Value, new DateTime(2026, 9, 21, 15, 0, 0, DateTimeKind.Utc), Day, Sense.Modules.Attendance.Domain.SelfCheckIns.PunchDirection.Out,
                Sense.Modules.Attendance.Domain.Punches.PunchSource.Device, null, null, null, null, null, false, null, null,
                new DateTime(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc)).Value,
        }).ToList();
        _punches.ListForRangeAsync(_tenantId, Day, Day, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(punchList);
        _planner.ResolveAsync(Arg.Any<Guid>(), Day, Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>()).Returns((PlannedDay?)null);

        var bad = await Create().ListAsync(_tenantId, Day, DailyStatus.Incalculable, null, false, 1, 100, TestContext.Current.CancellationToken);
        // One batched punch read for the page; the plan is resolved only for the incalculable rows.
        await _punches.Received(1).ListForRangeAsync(_tenantId, Day, Day, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>());
        await _planner.Received(2).ResolveAsync(Arg.Any<Guid>(), Day, Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>());
        bad.Items.Count.ShouldBe(2);
        bad.Items.ShouldAllBe(r => r.IncalculableReason == "noPlan");

        var arrived = await Create().ListAsync(_tenantId, Day, DailyStatus.Arrived, null, false, 1, 100, TestContext.Current.CancellationToken);
        arrived.Items.Single(r => r.EmployeeName == "Fine").IncalculableReason.ShouldBeNull();
        await _planner.Received(2).ResolveAsync(Arg.Any<Guid>(), Day, Arg.Any<TimeOnly?>(), Arg.Any<CancellationToken>());
    }
}
