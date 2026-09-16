using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>GET attendance/timesheet/team (S-8, tur 3 sözleşmesi): ekip haftalık ızgarası.</summary>
public sealed class TeamTimesheetFeaturesTests
{
    // 2026-09-21 is a Monday.
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeB = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();

    public TeamTimesheetFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _schedules.IsWorkingDayAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => ((DateOnly)call[1]).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    private GetTeamWeekHandler CreateHandler() => new(_tenant, _dataScope, _employees, _days, _schedules);

    [Fact]
    public async Task Handle_NoVisibleEmployees_ReturnsEmpty()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var result = await CreateHandler().Handle(new GetTeamWeekQuery(Monday, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        await _days.DidNotReceive().ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RecordedDay_ReportsWorkedMinutesStatusAndAnomalies()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });

        var lateDay = AttendanceDay.Record(
            _tenantId, _employeeA, Monday, new TimeOnly(9, 30), new TimeOnly(18, 0),
            new TimeOnly(9, 0), new TimeOnly(18, 0), plannedMinutes: 480, breakMinutes: 60, AttendanceDaySource.Manual);
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Monday, Monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { lateDay });

        var result = await CreateHandler().Handle(new GetTeamWeekQuery(Monday, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var row = result.Value.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(_employeeA);
        row.EmployeeName.ShouldBe("Ayşe Yılmaz");
        row.Days.Count.ShouldBe(7);

        var mondayEntry = row.Days.Single(d => d.Date == Monday);
        mondayEntry.AttendanceDayId.ShouldBe(lateDay.Id);
        mondayEntry.WorkedMinutes.ShouldBe(lateDay.WorkedMinutes);
        mondayEntry.Status.ShouldBe(AttendanceResolutionState.Unresolved);
        mondayEntry.Anomalies.ShouldContain(AttendanceAnomaly.LateArrival);

        var saturdayEntry = row.Days.Single(d => d.Date == Monday.AddDays(5));
        saturdayEntry.AttendanceDayId.ShouldBeNull();
        saturdayEntry.Status.ShouldBe(AttendanceResolutionState.Resolved);
        saturdayEntry.Anomalies.ShouldBeEmpty();

        var tuesdayEntry = row.Days.Single(d => d.Date == Monday.AddDays(1));
        tuesdayEntry.AttendanceDayId.ShouldBeNull();
        tuesdayEntry.Status.ShouldBe(AttendanceResolutionState.Unresolved);
        tuesdayEntry.Anomalies.ShouldContain(AttendanceAnomaly.NoData);
    }

    [Fact]
    public async Task Handle_OrgUnitFilter_IntersectsWithVisibleScope()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA, _employeeB });
        var orgUnitId = Guid.NewGuid();
        _employees.ListActiveEmployeeIdsInOrgUnitsAsync(_tenantId, Arg.Is<IReadOnlyCollection<Guid>>(u => u.Contains(orgUnitId)), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { _employeeB });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeB, "Can", "Demir", null, null, null, "Active") });
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Monday, Monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay>());

        var result = await CreateHandler().Handle(new GetTeamWeekQuery(Monday, orgUnitId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var row = result.Value.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(_employeeB); // _employeeA dışlandı - birimde değil.
    }

    [Fact]
    public async Task Handle_UnrestrictedScope_FallsBackToAllActiveEmployees()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)null);
        _employees.ListActiveEmployeeIdsAsync(_tenantId, Arg.Any<CancellationToken>()).Returns(new List<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Monday, Monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay>());

        var result = await CreateHandler().Handle(new GetTeamWeekQuery(Monday, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().EmployeeId.ShouldBe(_employeeA);
    }
}
