using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Equalization;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>
/// QA regression: per-employee Attendance reads/writes must honour the caller's data scope (a Manager holding
/// attendance.timesheet.view for their own team only must not read another team's employee).
/// </summary>
public sealed class AttendanceDataScopeTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _inScope = Guid.NewGuid();
    private readonly Guid _outOfScope = Guid.NewGuid();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();

    public AttendanceDataScopeTests()
    {
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _inScope, Arg.Any<CancellationToken>()).Returns(true);
        _dataScope.CanAccessEmployeeAsync(Arg.Any<string>(), _outOfScope, Arg.Any<CancellationToken>()).Returns(false);
        _days.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());
        _schedules.GetWorkingDaysAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<DateOnly>());
    }

    [Fact]
    public async Task ListAttendanceDays_InScope_ReturnsRows()
    {
        var result = await new ListAttendanceDaysHandler(_days, _dataScope, new InMemoryPunchRepository(), Substitute.For<ITenantContext>(), TestPlanner.From(_schedules))
            .Handle(new ListAttendanceDaysQuery(_inScope, Monday, Monday.AddDays(6)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ListAttendanceDays_OutOfScope_ReturnsForbiddenWithoutReadingData()
    {
        var result = await new ListAttendanceDaysHandler(_days, _dataScope, new InMemoryPunchRepository(), Substitute.For<ITenantContext>(), TestPlanner.From(_schedules))
            .Handle(new ListAttendanceDaysQuery(_outOfScope, Monday, Monday.AddDays(6)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
        await _days.DidNotReceiveWithAnyArgs().ListForEmployeeAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetTimesheetWeekSummary_OutOfScope_ReturnsForbidden()
    {
        var handler = new GetTimesheetWeekSummaryHandler(_days, _schedules, _dataScope);

        var forbidden = await handler.Handle(new GetTimesheetWeekSummaryQuery(_outOfScope, Monday), TestContext.Current.CancellationToken);
        var allowed = await handler.Handle(new GetTimesheetWeekSummaryQuery(_inScope, Monday), TestContext.Current.CancellationToken);

        forbidden.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task GetEqualizationStatus_OutOfScope_ReturnsForbiddenBeforeAnyWrite()
    {
        var unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
        var handler = new GetEqualizationStatusHandler(
            Substitute.For<ITenantContext>(), Substitute.For<IShiftSettingsRepository>(), Substitute.For<IEqualizationPeriodRepository>(), _days, unitOfWork,
            new FixedLocalClock(DateTime.UtcNow), _dataScope);

        var result = await handler.Handle(new GetEqualizationStatusQuery(_outOfScope, Monday), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task ListEmployeeWorkScheduleAssignments_OutOfScope_ReturnsForbidden()
    {
        var handler = new ListEmployeeWorkScheduleAssignmentsHandler(
            Substitute.For<IEmployeeWorkScheduleAssignmentRepository>(), Substitute.For<IWorkScheduleRepository>(), _dataScope);

        var forbidden = await handler.Handle(new ListEmployeeWorkScheduleAssignmentsQuery(_outOfScope), TestContext.Current.CancellationToken);
        var allowed = await handler.Handle(new ListEmployeeWorkScheduleAssignmentsQuery(_inScope), TestContext.Current.CancellationToken);

        forbidden.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
        allowed.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ListAnomalyResolutions_OutOfScopeDay_ReturnsForbidden()
    {
        var day = AttendanceDay.Record(_tenantId, _outOfScope, Monday, null, null, null, null, 0, 0, AttendanceDaySource.Manual);
        _days.GetByIdAsync(day.Id, Arg.Any<CancellationToken>()).Returns(day);

        var result = await new ListAnomalyResolutionsHandler(Substitute.For<IAnomalyResolutionRepository>(), _days, _dataScope)
            .Handle(new ListAnomalyResolutionsQuery(day.Id), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
    }

    [Fact]
    public async Task RecordAttendance_OutOfScope_ReturnsForbiddenWithoutWriting()
    {
        var handler = new RecordAttendanceHandler(
            _days, TestPlanner.From(_schedules), Substitute.For<ITenantContext>(), _dataScope, new FixedLocalClock(DateTime.UtcNow), new InMemoryPunchRepository(),
            Substitute.For<ICurrentUser>(), Substitute.For<IMonthlyTimesheetApprovalRepository>());

        var result = await handler.Handle(
            new RecordAttendanceCommand(_outOfScope, Monday, new TimeOnly(9, 0), new TimeOnly(17, 0)), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
        _days.DidNotReceiveWithAnyArgs().Add(default!);
    }
}

public sealed class ShiftAssignmentVisibilityTests
{
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Sunday = Monday.AddDays(6);
    private static readonly TimeOnly Nine = new(9, 0);
    private static readonly TimeOnly Five = new(17, 0);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _mine = Guid.NewGuid();
    private readonly Guid _teammate = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();
    private readonly IShiftAssignmentRepository _shifts = Substitute.For<IShiftAssignmentRepository>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();

    private ShiftAssignment Draft(Guid employeeId) => ShiftAssignment.Create(_tenantId, employeeId, Monday, Nine, Five, 30, false, null).Value;

    [Fact]
    public async Task List_NoFilter_QueriesOnlyVisibleEmployees()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _mine, _teammate });
        _shifts.ListForRangeAsync(Monday, Sunday, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment> { Draft(_teammate) });

        var result = await new ListShiftAssignmentsHandler(_shifts, _dataScope)
            .Handle(new ListShiftAssignmentsQuery(Monday, Sunday, null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _shifts.Received(1).ListForRangeAsync(
            Monday, Sunday, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 2 && ids.Contains(_mine) && ids.Contains(_teammate)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_RequestedIdsOutsideScope_AreDropped()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _mine });
        _shifts.ListForRangeAsync(Monday, Sunday, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(new List<ShiftAssignment>());

        await new ListShiftAssignmentsHandler(_shifts, _dataScope)
            .Handle(new ListShiftAssignmentsQuery(Monday, Sunday, [_mine, _stranger]), TestContext.Current.CancellationToken);

        await _shifts.Received(1).ListForRangeAsync(
            Monday, Sunday, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 1 && ids.Contains(_mine)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_OnlyOutOfScopeIdsRequested_ReturnsEmptyWithoutQuerying()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { _mine });

        var result = await new ListShiftAssignmentsHandler(_shifts, _dataScope)
            .Handle(new ListShiftAssignmentsQuery(Monday, Sunday, [_stranger]), TestContext.Current.CancellationToken);

        result.Value.ShouldBeEmpty();
        await _shifts.DidNotReceiveWithAnyArgs().ListForRangeAsync(default, default, default, default);
    }

    [Fact]
    public async Task List_NoVisibleEmployees_ReturnsEmptyWithoutQuerying()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid>());

        var result = await new ListShiftAssignmentsHandler(_shifts, _dataScope)
            .Handle(new ListShiftAssignmentsQuery(Monday, Sunday, null), TestContext.Current.CancellationToken);

        result.Value.ShouldBeEmpty();
        await _shifts.DidNotReceiveWithAnyArgs().ListForRangeAsync(default, default, default, default);
    }

    [Fact]
    public async Task List_CompanyScope_KeepsCallerFilterUntouched()
    {
        _dataScope.GetVisibleEmployeeIdsAsync(AttendancePermissions.ShiftView.Key, Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        _shifts.ListForRangeAsync(Monday, Sunday, null, Arg.Any<CancellationToken>()).Returns(new List<ShiftAssignment> { Draft(_stranger) });

        var result = await new ListShiftAssignmentsHandler(_shifts, _dataScope)
            .Handle(new ListShiftAssignmentsQuery(Monday, Sunday, null), TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetMyShifts_HidesUnpublishedDrafts_ButShowsPublished()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(_mine);
        var draft = Draft(_mine);
        var published = ShiftAssignment.Create(_tenantId, _mine, Monday.AddDays(1), Nine, Five, 30, false, null).Value;
        published.Publish(DateTime.UtcNow);
        _shifts.ListForRangeAsync(Monday, Sunday, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment> { draft, published });

        var result = await new GetMyShiftAssignmentsHandler(_shifts, currentUser)
            .Handle(new GetMyShiftAssignmentsQuery(Monday, Sunday), TestContext.Current.CancellationToken);

        result.Value.ShouldHaveSingleItem().Id.ShouldBe(published.Id);
    }
}
