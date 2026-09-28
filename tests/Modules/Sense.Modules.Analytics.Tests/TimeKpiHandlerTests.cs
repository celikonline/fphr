using NSubstitute;
using Sense.Modules.Analytics.Application.Kpis;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Analytics.Tests;

/// <summary>Zaman KPI'ları (zaman-devam Z1.3): Attendance okuyucusunun sayıları Analytics'te org'a göre gruplanır.</summary>
public sealed class TimeKpiHandlerTests
{
    private static readonly DateOnly From = new(2026, 9, 21);
    private static readonly DateOnly To = new(2026, 9, 27);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _deptTypeId = Guid.NewGuid();
    private readonly Guid _titleTypeId = Guid.NewGuid();
    private readonly Guid _deptA = Guid.NewGuid();
    private readonly Guid _deptB = Guid.NewGuid();
    private readonly Guid _titleX = Guid.NewGuid();
    private readonly Guid _emp1 = Guid.NewGuid();
    private readonly Guid _emp2 = Guid.NewGuid();
    private readonly Guid _emp3 = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _directory = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceDailyKpiReader _reader = Substitute.For<IAttendanceDailyKpiReader>();
    private readonly IEmployeeReportReader _employees = Substitute.For<IEmployeeReportReader>();
    private readonly IOrgUnitLookup _orgUnits = Substitute.For<IOrgUnitLookup>();
    private readonly TimeProvider _clock = TimeProvider.System;

    public TimeKpiHandlerTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.dashboard.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _directory.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { _emp1, _emp2, _emp3 });
        _orgUnits.ListActiveTypesAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(new List<OrgUnitTypeInfo>
        {
            new(_deptTypeId, "Department", "Departman", 2),
            new(_titleTypeId, "Title", "Unvan", 3),
        });
        _orgUnits.ListActiveUnitsAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(new List<OrgUnitInfo>
        {
            new(_deptA, _deptTypeId, null, "Depo"),
            new(_deptB, _deptTypeId, null, "Muhasebe"),
            new(_titleX, _titleTypeId, _deptA, "Operator"),
        });
        // emp1: Depo/Operator, emp2: Muhasebe (unvansız), emp3: yerleşimsiz.
        _employees.ListAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new List<EmployeeReportRow>
        {
            Row(_emp1, _titleX),
            Row(_emp2, _deptB),
            Row(_emp3, null),
        });
    }

    private static EmployeeReportRow Row(Guid id, Guid? orgUnitId) =>
        new(id, "A", "B", null, null, null, "Active", orgUnitId, null, null, null, null, null, "Unspecified", null, new Dictionary<Guid, Guid>());

    private static EmployeeAttendancePeriodDto Period(Guid id, int lateCount = 0, int lateMin = 0, int earlyCount = 0, int earlyMin = 0, int ot = 0, int planned = 0, int worked = 0) =>
        new(id, lateCount, lateMin, earlyCount, earlyMin, ot, planned, worked);

    private void ReturnPeriods(params EmployeeAttendancePeriodDto[] periods) =>
        _reader.EmployeeTotalsAsync(_tenantId, From, To, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(periods);

    [Fact]
    public async Task Seven_day_totals_fill_missing_days_with_zero()
    {
        var days = Enumerable.Range(0, 7).Select(i => new DailyAttendanceTotalsDto(From.AddDays(i), 0, 0, 0, 0, 0, 0)).ToList();
        _reader.DailyTotalsAsync(_tenantId, From, To, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(days);

        var result = await new GetAttendanceTrendKpiHandler(_tenant, _dataScope, _directory, _reader, _clock)
            .Handle(new GetAttendanceTrendKpiQuery(From, To), CancellationToken.None);

        result.Value.Count.ShouldBe(7);
        result.Value.ShouldAllBe(d => d.Planned == 0 && d.Arrived == 0);
    }

    [Fact]
    public async Task Late_early_grouped_by_unit_uses_org_lookup()
    {
        ReturnPeriods(Period(_emp1, 2, 40, 1, 10), Period(_emp2, 1, 5), Period(_emp3, earlyCount: 1, earlyMin: 15));

        var result = await new GetLateEarlyByUnitKpiHandler(_tenant, _dataScope, _directory, _reader, _employees, _orgUnits, _clock)
            .Handle(new GetLateEarlyByUnitKpiQuery(From, To), CancellationToken.None);

        result.Value.Count.ShouldBe(3);
        var depo = result.Value.Single(r => r.OrgUnitId == _deptA);
        (depo.OrgUnitName, depo.LateCount, depo.LateMinutes, depo.EarlyLeaveCount, depo.EarlyLeaveMinutes).ShouldBe(("Depo", 2, 40, 1, 10));
        result.Value.Single(r => r.OrgUnitId == _deptB).LateMinutes.ShouldBe(5);
        result.Value.Single(r => r.OrgUnitId is null).EarlyLeaveMinutes.ShouldBe(15);
    }

    [Fact]
    public async Task Overtime_grouped_by_title()
    {
        ReturnPeriods(Period(_emp1, ot: 90), Period(_emp2, ot: 30));

        var result = await new GetOvertimeByGroupKpiHandler(_tenant, _dataScope, _directory, _reader, _employees, _orgUnits, _clock)
            .Handle(new GetOvertimeByGroupKpiQuery("title", From, To), CancellationToken.None);

        var operatorRow = result.Value.Single(r => r.GroupId == _titleX);
        (operatorRow.GroupName, operatorRow.OvertimeMinutes, operatorRow.EmployeeCount).ShouldBe(("Operator", 90, 1));
        var unassigned = result.Value.Single(r => r.GroupId is null);
        (unassigned.GroupName, unassigned.OvertimeMinutes, unassigned.EmployeeCount).ShouldBe(("Unassigned", 30, 2));
        result.Value[0].GroupId.ShouldBe(_titleX);
    }

    [Fact]
    public async Task Shift_attendance_rate_is_worked_over_planned_days()
    {
        ReturnPeriods(Period(_emp1, planned: 5, worked: 4), Period(_emp2, planned: 3, worked: 1));

        var result = await new GetShiftAttendanceRateKpiHandler(_tenant, _dataScope, _directory, _reader, _clock)
            .Handle(new GetShiftAttendanceRateKpiQuery(From, To), CancellationToken.None);

        (result.Value.PlannedShiftDays, result.Value.WorkedShiftDays, result.Value.Rate).ShouldBe((8, 5, 0.625m));
    }

    [Fact]
    public async Task Shift_attendance_rate_is_zero_when_nothing_planned()
    {
        ReturnPeriods(Period(_emp1));

        var result = await new GetShiftAttendanceRateKpiHandler(_tenant, _dataScope, _directory, _reader, _clock)
            .Handle(new GetShiftAttendanceRateKpiQuery(From, To), CancellationToken.None);

        result.Value.Rate.ShouldBe(0m);
    }

    [Fact]
    public async Task Empty_scope_returns_empty_not_error()
    {
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.dashboard.view", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var trend = await new GetAttendanceTrendKpiHandler(_tenant, _dataScope, _directory, _reader, _clock).Handle(new GetAttendanceTrendKpiQuery(From, To), CancellationToken.None);
        var lateEarly = await new GetLateEarlyByUnitKpiHandler(_tenant, _dataScope, _directory, _reader, _employees, _orgUnits, _clock).Handle(new GetLateEarlyByUnitKpiQuery(From, To), CancellationToken.None);
        var overtime = await new GetOvertimeByGroupKpiHandler(_tenant, _dataScope, _directory, _reader, _employees, _orgUnits, _clock).Handle(new GetOvertimeByGroupKpiQuery("unit", From, To), CancellationToken.None);
        var rate = await new GetShiftAttendanceRateKpiHandler(_tenant, _dataScope, _directory, _reader, _clock).Handle(new GetShiftAttendanceRateKpiQuery(From, To), CancellationToken.None);

        trend.IsSuccess.ShouldBeTrue();
        trend.Value.ShouldBeEmpty();
        lateEarly.Value.ShouldBeEmpty();
        overtime.Value.ShouldBeEmpty();
        (rate.Value.PlannedShiftDays, rate.Value.Rate).ShouldBe((0, 0m));
        await _reader.DidNotReceiveWithAnyArgs().EmployeeTotalsAsync(default, default, default, default!, default);
    }

    [Fact]
    public void Queries_require_dashboard_view_permission()
    {
        foreach (var type in new[] { typeof(GetAttendanceTrendKpiQuery), typeof(GetLateEarlyByUnitKpiQuery), typeof(GetOvertimeByGroupKpiQuery), typeof(GetShiftAttendanceRateKpiQuery) })
        {
            type.GetCustomAttributes(typeof(RequiresPermissionAttribute), true).Cast<RequiresPermissionAttribute>()
                .Select(a => a.Permission).ShouldContain("attendance.dashboard.view");
        }
    }

    [Theory]
    [InlineData("branch", true)]
    [InlineData("Team", true)]
    [InlineData("bogus", false)]
    public void GroupBy_validator_accepts_only_known_axes(string groupBy, bool valid) =>
        new GetOvertimeByGroupKpiValidator().Validate(new GetOvertimeByGroupKpiQuery(groupBy, From, To)).IsValid.ShouldBe(valid);
}
