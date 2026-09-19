using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MonthlyTimesheet;
using Sense.Modules.Attendance.Application.SelfCheckIns;
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

/// <summary>QA regression: manual timesheet records cannot be dated in the future and monthly approval cannot close a month that has not started (tenant-local dates).</summary>
public sealed class TimesheetBoundaryTests
{
    // 22:30 UTC on the 18th is already 01:30 on the 19th in a UTC+3 tenant.
    private static readonly DateTime NowUtc = new(2026, 9, 18, 22, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime NowLocal = new(2026, 9, 19, 1, 30, 0);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ITenantLocalClock _localClock = new FixedLocalClock(NowUtc, NowLocal);

    public TimesheetBoundaryTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _schedules.GetForDateAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(17, 0), null, null, false));
    }

    private Task<Sense.Shared.Kernel.Results.Result<Guid>> Record(DateOnly date) =>
        new RecordAttendanceHandler(_days, _schedules, _tenant, _dataScope, _localClock)
            .Handle(new RecordAttendanceCommand(_employeeId, date, new TimeOnly(9, 0), new TimeOnly(17, 0)), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Record_FutureDate_IsRejectedWithoutWriting()
    {
        var result = await Record(new DateOnly(2026, 9, 20));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.DateInFuture);
        _days.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Record_TodayInTenantLocalTime_IsAccepted_EvenWhenUtcDateIsYesterday()
    {
        var result = await Record(new DateOnly(2026, 9, 19));

        result.IsSuccess.ShouldBeTrue();
        _days.Received(1).Add(Arg.Any<AttendanceDay>());
    }

    [Fact]
    public async Task Record_PastDate_IsAccepted()
    {
        var result = await Record(new DateOnly(2026, 9, 1));

        result.IsSuccess.ShouldBeTrue();
    }

    private ApproveMonthlyTimesheetHandler CreateApproveHandler()
    {
        var approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
        _days.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());
        var currentUser = Substitute.For<ICurrentUser>();
        return new ApproveMonthlyTimesheetHandler(
            approvals, _days, new LeaveOverlapChecker(Substitute.For<IApprovedLeaveDateLookup>()), _dataScope, _tenant, currentUser, TimeProvider.System, _localClock);
    }

    [Fact]
    public async Task ApproveMonth_FutureMonth_IsRejected()
    {
        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeId, new DateOnly(2026, 10, 1)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.PeriodNotStarted);
    }

    [Theory]
    [InlineData(2026, 9, 1)]   // current month (running) may be closed early
    [InlineData(2026, 8, 1)]   // past month
    [InlineData(2026, 9, 15)]  // any day of the month normalizes to its month start
    public async Task ApproveMonth_CurrentOrPastMonth_IsAccepted(int year, int month, int day)
    {
        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeId, new DateOnly(year, month, day)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ApproveAll_FutureMonth_IsRejected()
    {
        var employees = Substitute.For<IEmployeeDirectory>();
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeId });
        var handler = new ApproveAllMonthlyTimesheetsHandler(
            _tenant, Substitute.For<ICurrentUser>(), _dataScope, employees, Substitute.For<IMonthlyTimesheetApprovalRepository>(), _days,
            new LeaveOverlapChecker(Substitute.For<IApprovedLeaveDateLookup>()), TimeProvider.System, _localClock);

        var result = await handler.Handle(new ApproveAllMonthlyTimesheetsCommand(new DateOnly(2026, 10, 1), null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.PeriodNotStarted);
    }
}
