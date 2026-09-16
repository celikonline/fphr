using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class AnomalyResolutionFeaturesTests
{
    private static readonly TimeOnly PlannedStart = new(9, 0);
    private static readonly TimeOnly PlannedEnd = new(17, 0);
    private const int PlannedMinutes = 8 * 60;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IAnomalyResolutionRepository _resolutions = Substitute.For<IAnomalyResolutionRepository>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILeaveCommandService _leave = Substitute.For<ILeaveCommandService>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();

    public AnomalyResolutionFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.UserId.Returns(_userId);
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetResolveKey, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _days.ListForEmployeeAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay>());
        _schedules.GetWorkingDaysAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<DateOnly>());
    }

    private AttendanceDay UnderworkDay() =>
        AttendanceDay.Record(_tenantId, _employeeId, new DateOnly(2026, 9, 21), PlannedStart, new TimeOnly(16, 0), PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Manual);

    private AttendanceDay OverworkDay() =>
        AttendanceDay.Record(_tenantId, _employeeId, new DateOnly(2026, 9, 21), PlannedStart, new TimeOnly(19, 0), PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Manual);

    private void ExistingDay(AttendanceDay day) => _days.GetByIdAsync(day.Id, Arg.Any<CancellationToken>()).Returns(day);

    [Fact]
    public async Task SetWorkedHours_UnderworkDay_ResolvesAndRecordsResolution()
    {
        var day = UnderworkDay();
        ExistingDay(day);
        var handler = new SetWorkedHoursHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new SetWorkedHoursCommand(day.Id, PlannedMinutes, "Elle düzeltildi"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        day.OverrideWorkedMinutes.ShouldBe(PlannedMinutes);
        _resolutions.Received(1).Add(Arg.Is<AnomalyResolution>(r => r.Action == AnomalyResolutionAction.SetWorkedHours && r.AttendanceDayId == day.Id));
    }

    [Fact]
    public async Task SetWorkedHours_OverworkDay_NotAllowed()
    {
        var day = OverworkDay();
        ExistingDay(day);
        var handler = new SetWorkedHoursHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new SetWorkedHoursCommand(day.Id, PlannedMinutes, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.AnomalyActionNotAllowed);
        _resolutions.DidNotReceive().Add(Arg.Any<AnomalyResolution>());
    }

    [Fact]
    public async Task SetWorkedHours_EmployeeOutOfScope_ReturnsForbidden()
    {
        var day = UnderworkDay();
        ExistingDay(day);
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.TimesheetResolveKey, _employeeId, Arg.Any<CancellationToken>()).Returns(false);
        var handler = new SetWorkedHoursHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new SetWorkedHoursCommand(day.Id, PlannedMinutes, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotInScope);
    }

    [Fact]
    public async Task Ignore_FlaggedDay_SetsIgnoredAndRecordsResolution()
    {
        var day = OverworkDay();
        ExistingDay(day);
        var handler = new IgnoreAttendanceAnomalyHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new IgnoreAttendanceAnomalyCommand(day.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Ignored);
        _resolutions.Received(1).Add(Arg.Is<AnomalyResolution>(r => r.Action == AnomalyResolutionAction.Ignore));
    }

    [Fact]
    public async Task AddOvertime_OverworkDay_CreatesOvertimeAndResolves()
    {
        var day = OverworkDay();
        ExistingDay(day);
        var overtimeId = Guid.NewGuid();
        _leave.CreateOvertimeRequestAsync(_employeeId, day.Date, new TimeOnly(17, 0), 60, "Fazla mesai", Arg.Any<CancellationToken>()).Returns(overtimeId);
        var handler = new AddOvertimeFromAnomalyHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _leave, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new AddOvertimeFromAnomalyCommand(day.Id, new TimeOnly(17, 0), 60, "Fazla mesai"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        _resolutions.Received(1).Add(Arg.Is<AnomalyResolution>(r =>
            r.Action == AnomalyResolutionAction.AddOvertime && r.LinkedEntityType == "OvertimeRequest" && r.LinkedEntityId == overtimeId));
    }

    [Fact]
    public async Task AddOvertime_LeaveCallFails_DoesNotResolveDay()
    {
        var day = OverworkDay();
        ExistingDay(day);
        _leave.CreateOvertimeRequestAsync(_employeeId, day.Date, new TimeOnly(17, 0), 60, null, Arg.Any<CancellationToken>())
            .Returns(Error.Forbidden("leave.errors.employee_not_in_scope"));
        var handler = new AddOvertimeFromAnomalyHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _leave, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new AddOvertimeFromAnomalyCommand(day.Id, new TimeOnly(17, 0), 60, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Unresolved);
        _resolutions.DidNotReceive().Add(Arg.Any<AnomalyResolution>());
    }

    [Fact]
    public async Task AddOvertime_UnderworkOnlyDay_NotAllowed()
    {
        var day = UnderworkDay();
        ExistingDay(day);
        var handler = new AddOvertimeFromAnomalyHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _leave, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new AddOvertimeFromAnomalyCommand(day.Id, new TimeOnly(17, 0), 60, null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.AnomalyActionNotAllowed);
        await _leave.DidNotReceive().CreateOvertimeRequestAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateLeaveRequest_UnderworkDay_CreatesApprovedLeaveAndResolves()
    {
        var day = UnderworkDay();
        ExistingDay(day);
        var leaveTypeId = Guid.NewGuid();
        var leaveRequestId = Guid.NewGuid();
        _leave.CreateApprovedLeaveAsync(_employeeId, leaveTypeId, day.Date, day.Date, "Az çalışma çünkü izinli", Arg.Any<CancellationToken>()).Returns(leaveRequestId);
        var handler = new CreateLeaveRequestFromAnomalyHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _leave, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new CreateLeaveRequestFromAnomalyCommand(day.Id, leaveTypeId, "Az çalışma çünkü izinli"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        _resolutions.Received(1).Add(Arg.Is<AnomalyResolution>(r =>
            r.Action == AnomalyResolutionAction.CreateLeaveRequest && r.LinkedEntityType == "LeaveRequest" && r.LinkedEntityId == leaveRequestId));
    }

    [Fact]
    public async Task SetWorkedHours_LastOpenDayOfWeek_PublishesTimesheetWeekCompleted()
    {
        var day = UnderworkDay();
        ExistingDay(day);
        _schedules.GetWorkingDaysAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<DateOnly> { day.Date });
        _days.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay> { day });
        var handler = new SetWorkedHoursHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _schedules, _outbox, TimeProvider.System);

        await handler.Handle(new SetWorkedHoursCommand(day.Id, PlannedMinutes, null), TestContext.Current.CancellationToken);

        _outbox.Received(1).Enqueue(Arg.Is<Sense.Modules.Attendance.Contracts.Events.TimesheetWeekCompleted>(e => e.EmployeeId == _employeeId));
    }

    [Fact]
    public async Task SetWorkedHours_OtherDayInWeekStillUnresolved_DoesNotPublish()
    {
        var day = UnderworkDay();
        var stillOpen = AttendanceDay.Record(
            _tenantId, _employeeId, day.Date.AddDays(1), PlannedStart, new TimeOnly(19, 0), PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Manual);
        ExistingDay(day);
        _schedules.GetWorkingDaysAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<DateOnly> { day.Date, stillOpen.Date });
        _days.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay> { day, stillOpen });
        var handler = new SetWorkedHoursHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _schedules, _outbox, TimeProvider.System);

        await handler.Handle(new SetWorkedHoursCommand(day.Id, PlannedMinutes, null), TestContext.Current.CancellationToken);

        _outbox.DidNotReceive().Enqueue(Arg.Any<Sense.Modules.Attendance.Contracts.Events.TimesheetWeekCompleted>());
    }

    [Fact]
    public async Task CreateLeaveRequest_AlreadyResolvedDay_Fails()
    {
        var day = UnderworkDay();
        day.ResolveWithOverride(PlannedMinutes, null);
        ExistingDay(day);
        var handler = new CreateLeaveRequestFromAnomalyHandler(_days, _resolutions, _dataScope, _tenant, _currentUser, _leave, _schedules, _outbox, TimeProvider.System);

        var result = await handler.Handle(new CreateLeaveRequestFromAnomalyCommand(day.Id, Guid.NewGuid(), null), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.DayAlreadyResolved);
        await _leave.DidNotReceive().CreateApprovedLeaveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
