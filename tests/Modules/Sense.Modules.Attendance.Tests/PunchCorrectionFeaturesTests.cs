using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Corrections;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Corrections;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Events;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>FG-004 eksik giriş/çıkış düzeltme talebi: submit/withdraw/list handlers and the Workflow decision handlers.</summary>
public sealed class PunchCorrectionFeaturesTests
{
    private static readonly DateOnly Day = new(2026, 9, 21);
    private static readonly DateTime NowUtc = new(2026, 9, 28, 7, 0, 0, DateTimeKind.Utc);
    private static readonly TimeOnly PlannedStart = new(9, 0);
    private static readonly TimeOnly PlannedEnd = new(18, 0);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _approvalId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPunchCorrectionRequestRepository _corrections = Substitute.For<IPunchCorrectionRequestRepository>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IShiftPlanLockRepository _locks = Substitute.For<IShiftPlanLockRepository>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IShiftSettingsRepository _shiftSettings = Substitute.For<IShiftSettingsRepository>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly FixedLocalClock _localClock = new(NowUtc);
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(NowUtc));

    public PunchCorrectionFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _workflow.StartAsync(
                ApprovalRequestTypes.PunchCorrection, Arg.Any<Guid>(), _employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(_approvalId));
        _workflow.CancelAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _schedules.GetForDateAsync(_employeeId, Day, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, PlannedStart, PlannedEnd, null, null, false));
        _schedules.GetWorkingDaysAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _days.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private SubmitMyPunchCorrectionHandler Submitter() =>
        new(_tenant, _currentUser, _corrections, _days, _locks, _approvals, _workflow, _localClock, _clock);

    private PunchCorrectionApprovalCompletedHandler Applier() =>
        new(_corrections, _days, _schedules, _shiftSettings, _locks, _approvals, _outbox, _unitOfWork, _clock);

    private AttendanceDay MissingCheckOutDay() =>
        AttendanceDay.Record(_tenantId, _employeeId, Day, PlannedStart, null, PlannedStart, PlannedEnd, 540, 0, AttendanceDaySource.SelfService);

    private PunchCorrectionRequest PendingRequest(Guid employeeId, TimeOnly? checkIn, TimeOnly? checkOut)
    {
        var request = PunchCorrectionRequest.Submit(_tenantId, employeeId, Day, checkIn, checkOut, "Kart okutmayı unuttum", null, null, NowUtc).Value;
        request.AttachApproval(_approvalId);
        _corrections.FindByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        return request;
    }

    [Fact]
    public async Task Submit_StartsPunchCorrectionApproval_AndAddsPendingRequestWithTheDaySnapshot()
    {
        _days.FindByEmployeeAndDateAsync(_employeeId, Day, Arg.Any<CancellationToken>()).Returns(MissingCheckOutDay());

        var result = await Submitter().Handle(
            new SubmitMyPunchCorrectionCommand(Day, null, PlannedEnd, "Kart okutmayı unuttum"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
        result.Value.Status.ShouldBe(PunchCorrectionStatus.Pending);
        result.Value.Date.ShouldBe(Day);
        result.Value.RequestedCheckOut.ShouldBe(PlannedEnd);
        result.Value.CurrentCheckIn.ShouldBe(PlannedStart);
        result.Value.CurrentCheckOut.ShouldBeNull();
        result.Value.ApprovalRequestId.ShouldBe(_approvalId);
        await _workflow.Received(1).StartAsync(
            ApprovalRequestTypes.PunchCorrection, result.Value.Id, _employeeId, null, Arg.Any<string?>(), Arg.Is<string?>(agent => agent == null), Arg.Any<CancellationToken>());
        _corrections.Received(1).Add(Arg.Is<PunchCorrectionRequest>(r => r.Id == result.Value.Id && r.ApprovalRequestId == _approvalId));
    }

    [Fact]
    public async Task Submit_FutureDate_IsRejected()
    {
        var tomorrow = DateOnly.FromDateTime(NowUtc).AddDays(1);

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(tomorrow, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.CorrectionFutureDate);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, TestContext.Current.CancellationToken);
        _corrections.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Submit_LockedShiftPlanMonth_IsRejected()
    {
        _locks.FindByPeriodAsync(Day, Arg.Any<CancellationToken>())
            .Returns(ShiftPlanLock.Lock(_tenantId, Day, NowUtc, null, ShiftPlanLockSource.Manual));

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.CorrectionPeriodLocked);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _corrections.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Submit_UnlockedShiftPlanMonth_IsAllowed()
    {
        var plan = ShiftPlanLock.Lock(_tenantId, Day, NowUtc, null, ShiftPlanLockSource.Manual);
        plan.Unlock(NowUtc, null, "düzeltme").IsSuccess.ShouldBeTrue();
        _locks.FindByPeriodAsync(Day, Arg.Any<CancellationToken>()).Returns(plan);

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public async Task Submit_ApprovedMonthlyTimesheet_IsRejectedAsLocked()
    {
        _approvals.FindAsync(_employeeId, Day, Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, Day, NowUtc, null));

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.CorrectionPeriodLocked);
    }

    [Fact]
    public async Task Submit_SecondOpenRequestForTheSameDay_IsRejected()
    {
        _corrections.HasPendingAsync(_employeeId, Day, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.CorrectionPendingExists);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Submit_WithoutTimes_IsRejected_BeforeStartingAnApproval()
    {
        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, null, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionTimesRequired);
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Submit_WorkflowFailure_AddsNothing()
    {
        _workflow.StartAsync(
                ApprovalRequestTypes.PunchCorrection, Arg.Any<Guid>(), _employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(Error.Validation("workflow.request.no_approver")));

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("workflow.request.no_approver");
        _corrections.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Submit_WithoutEmployeeRecord_IsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        var result = await Submitter().Handle(new SubmitMyPunchCorrectionCommand(Day, PlannedStart, null, "gerekçe"), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task Withdraw_SomeoneElsesRequest_LooksLikeAnUnknownOne()
    {
        var foreign = PendingRequest(Guid.NewGuid(), PlannedStart, null);
        var handler = new WithdrawMyPunchCorrectionHandler(_currentUser, _corrections, _workflow, _clock);

        var result = await handler.Handle(new WithdrawMyPunchCorrectionCommand(foreign.Id), TestContext.Current.CancellationToken);
        var unknown = await handler.Handle(new WithdrawMyPunchCorrectionCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionNotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        unknown.Error.ShouldBe(result.Error);
        foreign.Status.ShouldBe(PunchCorrectionStatus.Pending);
        await _workflow.DidNotReceiveWithAnyArgs().CancelAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Withdraw_OwnPendingRequest_CancelsTheApproval()
    {
        var own = PendingRequest(_employeeId, PlannedStart, null);
        var handler = new WithdrawMyPunchCorrectionHandler(_currentUser, _corrections, _workflow, _clock);

        var result = await handler.Handle(new WithdrawMyPunchCorrectionCommand(own.Id), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        own.Status.ShouldBe(PunchCorrectionStatus.Withdrawn);
        await _workflow.Received(1).CancelAsync(_approvalId, _employeeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Withdraw_AlreadyDecidedRequest_IsRejected()
    {
        var own = PendingRequest(_employeeId, PlannedStart, null);
        own.Approve(_approvalId, NowUtc).IsSuccess.ShouldBeTrue();
        var handler = new WithdrawMyPunchCorrectionHandler(_currentUser, _corrections, _workflow, _clock);

        var result = await handler.Handle(new WithdrawMyPunchCorrectionCommand(own.Id), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe(AttendanceDomainErrors.CorrectionNotPending);
        await _workflow.DidNotReceiveWithAnyArgs().CancelAsync(default, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task List_ReversedRange_IsRejected_AndOtherwiseMapsTheCallersRequests()
    {
        var own = PendingRequest(_employeeId, PlannedStart, null);
        _corrections.ListForEmployeeAsync(_employeeId, Day, Day, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([own]);
        var handler = new ListMyPunchCorrectionsHandler(_currentUser, _corrections);

        var reversed = await handler.Handle(new ListMyPunchCorrectionsQuery(Day, Day.AddDays(-1)), TestContext.Current.CancellationToken);
        var listed = await handler.Handle(new ListMyPunchCorrectionsQuery(Day, Day), TestContext.Current.CancellationToken);

        reversed.Error.Code.ShouldBe(AttendanceErrors.DateRangeInvalid);
        listed.Value.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        listed.Value[0].RequestedCheckIn.ShouldBe(PlannedStart);
    }

    [Fact]
    public async Task ApprovalCompleted_WritesTheRequestedTimeToTheDay_AndRecomputesIt()
    {
        var day = MissingCheckOutDay();
        day.Anomalies.ShouldBe(AttendanceAnomaly.MissingCheckOut);
        _days.FindByEmployeeAndDateAsync(_employeeId, Day, Arg.Any<CancellationToken>()).Returns(day);
        var request = PendingRequest(_employeeId, null, PlannedEnd);

        await Applier().Handle(
            new ApprovalCompleted(_tenantId, _approvalId, ApprovalRequestTypes.PunchCorrection, request.Id, _employeeId), TestContext.Current.CancellationToken);

        request.Status.ShouldBe(PunchCorrectionStatus.Approved);
        day.CheckIn.ShouldBe(PlannedStart);
        day.CheckOut.ShouldBe(PlannedEnd);
        day.Source.ShouldBe(AttendanceDaySource.Manual);
        day.Anomalies.ShouldBe(AttendanceAnomaly.None);
        day.ResolutionState.ShouldBe(AttendanceResolutionState.Resolved);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalCompleted_MonthLockedMeanwhile_MarksApplyFailed_AndLeavesTheDayAlone()
    {
        var day = MissingCheckOutDay();
        _days.FindByEmployeeAndDateAsync(_employeeId, Day, Arg.Any<CancellationToken>()).Returns(day);
        _locks.FindByPeriodAsync(Day, Arg.Any<CancellationToken>())
            .Returns(ShiftPlanLock.Lock(_tenantId, Day, NowUtc, null, ShiftPlanLockSource.Auto));
        var request = PendingRequest(_employeeId, null, PlannedEnd);

        await Applier().Handle(
            new ApprovalCompleted(_tenantId, _approvalId, ApprovalRequestTypes.PunchCorrection, request.Id, _employeeId), TestContext.Current.CancellationToken);

        request.Status.ShouldBe(PunchCorrectionStatus.ApplyFailed);
        request.DecidedAt.ShouldBe(NowUtc);
        day.CheckOut.ShouldBeNull();
        day.Anomalies.ShouldBe(AttendanceAnomaly.MissingCheckOut);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApprovalCompleted_MonthlyTimesheetApprovedMeanwhile_MarksApplyFailed()
    {
        _approvals.FindAsync(_employeeId, Day, Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, Day, NowUtc, null));
        var request = PendingRequest(_employeeId, null, PlannedEnd);

        await Applier().Handle(
            new ApprovalCompleted(_tenantId, _approvalId, ApprovalRequestTypes.PunchCorrection, request.Id, _employeeId), TestContext.Current.CancellationToken);

        request.Status.ShouldBe(PunchCorrectionStatus.ApplyFailed);
        _days.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task ApprovalCompleted_TimesThatNoLongerFitTheDay_MarksApplyFailed()
    {
        // The day got a 08:00 check-out after the request; the requested 10:00 check-in would come after it.
        var day = AttendanceDay.Record(_tenantId, _employeeId, Day, null, new TimeOnly(8, 0), PlannedStart, PlannedEnd, 540, 0, AttendanceDaySource.Manual);
        _days.FindByEmployeeAndDateAsync(_employeeId, Day, Arg.Any<CancellationToken>()).Returns(day);
        var request = PendingRequest(_employeeId, new TimeOnly(10, 0), null);

        await Applier().Handle(
            new ApprovalCompleted(_tenantId, _approvalId, ApprovalRequestTypes.PunchCorrection, request.Id, _employeeId), TestContext.Current.CancellationToken);

        request.Status.ShouldBe(PunchCorrectionStatus.ApplyFailed);
        day.CheckIn.ShouldBeNull();
    }

    [Fact]
    public async Task ApprovalCompleted_ForAnotherRequestType_IsIgnored()
    {
        await Applier().Handle(
            new ApprovalCompleted(_tenantId, _approvalId, ApprovalRequestTypes.Overtime, Guid.NewGuid(), _employeeId), TestContext.Current.CancellationToken);

        await _corrections.DidNotReceiveWithAnyArgs().FindByIdAsync(default, TestContext.Current.CancellationToken);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApprovalRejected_MarksRejected_WithoutTouchingTheDay()
    {
        var request = PendingRequest(_employeeId, null, PlannedEnd);
        var handler = new PunchCorrectionApprovalRejectedHandler(_corrections, _unitOfWork, _clock);

        await handler.Handle(
            new ApprovalRejected(_tenantId, _approvalId, ApprovalRequestTypes.PunchCorrection, request.Id, _employeeId, 1), TestContext.Current.CancellationToken);

        request.Status.ShouldBe(PunchCorrectionStatus.Rejected);
        await _days.DidNotReceiveWithAnyArgs().FindByEmployeeAndDateAsync(default, default, TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
