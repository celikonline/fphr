using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.MonthlyTimesheet;
using Sense.Modules.Attendance.Application.SelfCheckIns;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Equalization;
using Sense.Modules.Attendance.Domain.Shifts;
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
        new RecordAttendanceHandler(_days, new PlannedDayResolver(_schedules, Substitute.For<IShiftAssignmentRepository>(), Substitute.For<IShiftDefinitionRepository>()), _tenant, _dataScope, _localClock)
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
    [InlineData(2026, 8, 1)]   // past month
    [InlineData(2026, 8, 15)]  // any day of the month normalizes to its month start
    public async Task ApproveMonth_PastMonth_IsAccepted(int year, int month, int day)
    {
        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeId, new DateOnly(year, month, day)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Y-3 (ik-surec-testi 2026-09-20): the still-running month (tenant-local 19 Sep) cannot be closed early.</summary>
    [Theory]
    [InlineData(2026, 9, 1)]
    [InlineData(2026, 9, 15)]
    public async Task ApproveMonth_RunningMonth_IsRejected(int year, int month, int day)
    {
        var result = await CreateApproveHandler().Handle(new ApproveMonthlyTimesheetCommand(_employeeId, new DateOnly(year, month, day)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.MonthNotComplete);
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

    /// <summary>Y-3 parity: "Tümünü Onayla" cannot close the still-running month either.</summary>
    [Fact]
    public async Task ApproveAll_RunningMonth_IsRejected()
    {
        var employees = Substitute.For<IEmployeeDirectory>();
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.TimesheetApproveKey, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _employeeId });
        var handler = new ApproveAllMonthlyTimesheetsHandler(
            _tenant, Substitute.For<ICurrentUser>(), _dataScope, employees, Substitute.For<IMonthlyTimesheetApprovalRepository>(), _days,
            new LeaveOverlapChecker(Substitute.For<IApprovedLeaveDateLookup>()), TimeProvider.System, _localClock);

        var result = await handler.Handle(new ApproveAllMonthlyTimesheetsCommand(new DateOnly(2026, 9, 1), null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.MonthNotComplete);
    }
}

/// <summary>QA regression: GetEqualizationStatus decides "period ended" on the tenant-local date, not the UTC date.</summary>
public sealed class EqualizationStatusLocalDateTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    private async Task<Application.Equalization.EqualizationStatusDto> QueryAsync(ITenantLocalClock clock)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(_tenantId);
        var settings = ShiftSettings.CreateDefault(_tenantId);
        settings.UpdateEqualization(enabled: true, periodMonths: 2, anchorMonth: 1, hasWrittenAgreement: false).IsSuccess.ShouldBeTrue();
        var settingsRepository = Substitute.For<IShiftSettingsRepository>();
        settingsRepository.FindAsync(Arg.Any<CancellationToken>()).Returns(settings);
        var days = Substitute.For<IAttendanceDayRepository>();
        days.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());
        var dataScope = Substitute.For<IDataScopeService>();
        dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _employeeId, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new Application.Equalization.GetEqualizationStatusHandler(
            tenant, settingsRepository, Substitute.For<IEqualizationPeriodRepository>(), days, Substitute.For<IAttendanceUnitOfWork>(), clock, dataScope);

        // Jul-Aug is the 2-month period containing 2026-08-15; it ends 2026-08-31.
        var result = await handler.Handle(new Application.Equalization.GetEqualizationStatusQuery(_employeeId, new DateOnly(2026, 8, 15)), TestContext.Current.CancellationToken);
        return result.Value;
    }

    [Fact]
    public async Task PeriodEndedInTenantLocalTimeButNotYetInUtc_IsClosed()
    {
        // 22:30 UTC on 31 Aug is 01:30 on 1 Sep in a UTC+3 tenant: the period has ended for that tenant.
        var status = await QueryAsync(new FixedLocalClock(new DateTime(2026, 8, 31, 22, 30, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 1, 30, 0)));

        status.PeriodEnd.ShouldBe(new DateOnly(2026, 8, 31));
        status.IsClosed.ShouldBeTrue();
    }

    [Fact]
    public async Task LastDayOfPeriodInTenantLocalTime_StaysOpen()
    {
        var status = await QueryAsync(new FixedLocalClock(new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 31, 15, 0, 0)));

        status.IsClosed.ShouldBeFalse();
    }
}
