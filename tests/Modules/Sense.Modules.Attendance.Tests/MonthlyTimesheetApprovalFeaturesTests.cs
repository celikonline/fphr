using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MonthlyTimesheet;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class MonthlyTimesheetApprovalFeaturesTests
{
    private static readonly DateOnly PeriodMonth = new(2026, 9, 1);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeB = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IAttendanceMonthlySummaryReader _attendanceSummaries = Substitute.For<IAttendanceMonthlySummaryReader>();
    private readonly ILeaveDayReader _leaveDays = Substitute.For<ILeaveDayReader>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IApprovedLeaveDateLookup _leaveDates = Substitute.For<IApprovedLeaveDateLookup>();
    private readonly LeaveOverlapChecker _leaveOverlap;
    private readonly TimeProvider _clock = TimeProvider.System;

    public MonthlyTimesheetApprovalFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.UserId.Returns(_userId);
        _leaveOverlap = new LeaveOverlapChecker(_leaveDates);
        _days.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());
        _days.ListForEmployeesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());
    }

    private GetMonthlyTimesheetHandler CreateQueryHandler() => new(_tenant, _dataScope, _employees, _attendanceSummaries, _leaveDays, _approvals);

    private ApproveMonthlyTimesheetHandler CreateApproveHandler() => new(_approvals, _days, _leaveOverlap, _dataScope, _tenant, _currentUser, _clock);

    private ApproveAllMonthlyTimesheetsHandler CreateApproveAllHandler() => new(_tenant, _currentUser, _dataScope, _employees, _approvals, _days, _leaveOverlap, _clock);

    [Fact]
    public async Task Query_NoVisibleEmployees_ReturnsEmpty()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var result = await CreateQueryHandler().Handle(new GetMonthlyTimesheetQuery(PeriodMonth, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Query_EmployeeWithNoDataAtAll_StillReturnsZeroedRow()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });
        _attendanceSummaries.GetAsync(_tenantId, PeriodMonth.Year, PeriodMonth.Month, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceMonthlySummary>());
        _leaveDays.GetMonthlySummaryAsync(_tenantId, PeriodMonth, PeriodMonth.AddMonths(1).AddDays(-1), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<LeaveDaySummary>());
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>())
            .Returns(new List<MonthlyTimesheetApproval>());

        var result = await CreateQueryHandler().Handle(new GetMonthlyTimesheetQuery(PeriodMonth, null), TestContext.Current.CancellationToken);

        var row = result.Value.ShouldHaveSingleItem();
        row.EmployeeId.ShouldBe(_employeeA);
        row.EmployeeName.ShouldBe("Ayşe Yılmaz");
        row.WorkedDays.ShouldBe(0);
        row.PaidLeaveDays.ShouldBe(0);
        row.IsApproved.ShouldBeFalse();
        row.ApprovedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Query_CombinesAttendanceLeaveAndApprovalSources()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.GetContactsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<EmployeeContact> { new(_employeeA, "Ayşe", "Yılmaz", null, null, null, "Active") });
        _attendanceSummaries.GetAsync(_tenantId, PeriodMonth.Year, PeriodMonth.Month, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceMonthlySummary> { new(_employeeA, 20, 160, 1, 5, true) });
        _leaveDays.GetMonthlySummaryAsync(_tenantId, PeriodMonth, PeriodMonth.AddMonths(1).AddDays(-1), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<LeaveDaySummary> { new(_employeeA, 2, 0, 1) });
        var approvedAt = new DateTime(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
        var approvedBy = Guid.NewGuid();
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>())
            .Returns(new List<MonthlyTimesheetApproval> { MonthlyTimesheetApproval.Approve(_tenantId, _employeeA, PeriodMonth, approvedAt, approvedBy) });

        var result = await CreateQueryHandler().Handle(new GetMonthlyTimesheetQuery(PeriodMonth, null), TestContext.Current.CancellationToken);

        var row = result.Value.ShouldHaveSingleItem();
        row.WorkedDays.ShouldBe(20);
        row.WorkedHours.ShouldBe(160);
        row.MissingDays.ShouldBe(1);
        row.OvertimeHours.ShouldBe(5);
        row.PaidLeaveDays.ShouldBe(2);
        row.SickLeaveDays.ShouldBe(1);
        row.IsPeriodLocked.ShouldBeTrue();
        row.IsApproved.ShouldBeTrue();
        row.ApprovedAt.ShouldBe(approvedAt);
        row.ApprovedByUserId.ShouldBe(approvedBy);
    }

    [Fact]
    public async Task Approve_OutOfScope_ReturnsForbidden()
    {
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetApproveKey, _employeeA, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeA, PeriodMonth), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
        _approvals.DidNotReceive().Add(Arg.Any<MonthlyTimesheetApproval>());
    }

    [Fact]
    public async Task Approve_NoExistingApproval_CreatesNewRow()
    {
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetApproveKey, _employeeA, Arg.Any<CancellationToken>()).Returns(true);
        _approvals.FindAsync(_employeeA, PeriodMonth, Arg.Any<CancellationToken>()).Returns((MonthlyTimesheetApproval?)null);

        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeA, PeriodMonth), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _approvals.Received(1).Add(Arg.Is<MonthlyTimesheetApproval>(a => a.EmployeeId == _employeeA && a.ApprovedByUserId == _userId));
    }

    [Fact]
    public async Task Approve_ExistingApproval_ReapprovesSameRow()
    {
        var existing = MonthlyTimesheetApproval.Approve(_tenantId, _employeeA, PeriodMonth, DateTime.UtcNow.AddDays(-5), Guid.NewGuid());
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetApproveKey, _employeeA, Arg.Any<CancellationToken>()).Returns(true);
        _approvals.FindAsync(_employeeA, PeriodMonth, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeA, PeriodMonth), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        existing.ApprovedByUserId.ShouldBe(_userId);
        _approvals.DidNotReceive().Add(Arg.Any<MonthlyTimesheetApproval>());
    }

    [Fact]
    public async Task ApproveAll_SkipsAlreadyApprovedEmployees()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA, _employeeB });
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>())
            .Returns(new List<MonthlyTimesheetApproval> { MonthlyTimesheetApproval.Approve(_tenantId, _employeeA, PeriodMonth, DateTime.UtcNow, Guid.NewGuid()) });

        var result = await CreateApproveAllHandler().Handle(new ApproveAllMonthlyTimesheetsCommand(PeriodMonth, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(1);
        _approvals.Received(1).Add(Arg.Is<MonthlyTimesheetApproval>(a => a.EmployeeId == _employeeB));
    }

    [Fact]
    public async Task ApproveAll_NoneVisible_DoesNothing()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var result = await CreateApproveAllHandler().Handle(new ApproveAllMonthlyTimesheetsCommand(PeriodMonth, null), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(0);
        _approvals.DidNotReceive().Add(Arg.Any<MonthlyTimesheetApproval>());
    }

    private AttendanceDay PunchedDay(Guid employeeId, DateOnly date) => AttendanceDay.Record(
        _tenantId, employeeId, date, new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual);

    [Fact]
    public async Task Approve_RunsLeaveCrossCheckBeforeApproving()
    {
        var date = new DateOnly(2026, 9, 21);
        var day = PunchedDay(_employeeA, date);
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetApproveKey, _employeeA, Arg.Any<CancellationToken>()).Returns(true);
        _days.ListForEmployeeAsync(_employeeA, PeriodMonth, new DateOnly(2026, 9, 30), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay> { day });
        _leaveDates.GetApprovedLeaveDatesAsync(_tenantId, PeriodMonth, new DateOnly(2026, 9, 30), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovedLeaveDay> { new(_employeeA, date) });

        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeA, date), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        day.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeTrue();
    }

    [Fact]
    public async Task ApproveAll_RunsLeaveCrossCheckOnlyForNewlyApprovedEmployees()
    {
        var date = new DateOnly(2026, 9, 21);
        var dayB = PunchedDay(_employeeB, date);
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA, _employeeB });
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>())
            .Returns(new List<MonthlyTimesheetApproval> { MonthlyTimesheetApproval.Approve(_tenantId, _employeeA, PeriodMonth, DateTime.UtcNow, Guid.NewGuid()) });
        _days.ListForEmployeesAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(_employeeB)), PeriodMonth, new DateOnly(2026, 9, 30), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { dayB });
        _leaveDates.GetApprovedLeaveDatesAsync(_tenantId, PeriodMonth, new DateOnly(2026, 9, 30), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ApprovedLeaveDay> { new(_employeeB, date) });

        var result = await CreateApproveAllHandler().Handle(new ApproveAllMonthlyTimesheetsCommand(date, null), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(1);
        dayB.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeTrue();
    }

    [Fact]
    public async Task ApproveAll_ScopedCaller_NeverTouchesEmployeesOutsideScope()
    {
        // Manager: visible set is their own team only - the company-wide directory list must not be consulted.
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>()).Returns(new List<MonthlyTimesheetApproval>());

        var result = await CreateApproveAllHandler().Handle(new ApproveAllMonthlyTimesheetsCommand(PeriodMonth, null), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(1);
        _approvals.Received(1).Add(Arg.Is<MonthlyTimesheetApproval>(a => a.EmployeeId == _employeeA));
        _approvals.DidNotReceive().Add(Arg.Is<MonthlyTimesheetApproval>(a => a.EmployeeId == _employeeB));
        await _employees.DidNotReceive().ListActiveEmployeeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAll_OrgUnitFilter_IntersectsWithCallerScope()
    {
        var unitId = Guid.NewGuid();
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _employees.ListActiveEmployeeIdsInOrgUnitsAsync(_tenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { _employeeA, _employeeB });
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>()).Returns(new List<MonthlyTimesheetApproval>());

        var result = await CreateApproveAllHandler().Handle(new ApproveAllMonthlyTimesheetsCommand(PeriodMonth, unitId), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(1);
        _approvals.DidNotReceive().Add(Arg.Is<MonthlyTimesheetApproval>(a => a.EmployeeId == _employeeB));
    }

    [Fact]
    public async Task ApproveAll_EveryoneAlreadyApproved_ReturnsZeroWithoutLeaveScan()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeA });
        _approvals.ListForPeriodAsync(Arg.Any<IReadOnlyCollection<Guid>>(), PeriodMonth, Arg.Any<CancellationToken>())
            .Returns(new List<MonthlyTimesheetApproval> { MonthlyTimesheetApproval.Approve(_tenantId, _employeeA, PeriodMonth, DateTime.UtcNow, Guid.NewGuid()) });

        var result = await CreateApproveAllHandler().Handle(new ApproveAllMonthlyTimesheetsCommand(PeriodMonth, null), TestContext.Current.CancellationToken);

        result.Value.ShouldBe(0);
        await _leaveDates.DidNotReceive().GetApprovedLeaveDatesAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
