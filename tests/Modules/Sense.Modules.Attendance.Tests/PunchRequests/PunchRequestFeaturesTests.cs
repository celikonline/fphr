using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.PunchRequests;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.PunchRequests;
using Sense.Modules.Attendance.Domain.SelfCheckIns;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Workflow.Contracts;
using Sense.Modules.Workflow.Contracts.Events;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.PunchRequests;

/// <summary>Eksik kayıt talebi (Z2.1): oluşturma kuralları, iptal ve Workflow karar handler'ları.</summary>
public sealed class PunchRequestFeaturesTests
{
    private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly WorkDate = new(2026, 9, 24);
    private static readonly DateTime Occurred = new(2026, 9, 24, 8, 30, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPunchRequestRepository _requests = Substitute.For<IPunchRequestRepository>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly IWorkflowService _workflow = Substitute.For<IWorkflowService>();
    private readonly IPunchRecorder _recorder = Substitute.For<IPunchRecorder>();
    private readonly IAttendanceDayRecalculator _recalculator = Substitute.For<IAttendanceDayRecalculator>();
    private readonly IAttendanceUnitOfWork _unitOfWork = Substitute.For<IAttendanceUnitOfWork>();
    private readonly IEmployeeDirectory _employees = Substitute.For<IEmployeeDirectory>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly TimeProvider _clock = new FixedTimeProvider(Now);

    public PunchRequestFeaturesTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _workflow.StartAsync(default!, default, default, default, default, default, default)
            .ReturnsForAnyArgs(Result.Success(Guid.NewGuid()));
        _requests.ListForEmployeeAsync(default, default, default, default).ReturnsForAnyArgs([]);
        _employees.GetContactsAsync(default, default!, default).ReturnsForAnyArgs(
            [new EmployeeContact(_employeeId, "Ayşe", "Yılmaz", null, null, null, "active")]);
    }

    private CreatePunchRequestHandler CreateHandler() => new(_tenant, _currentUser, _requests, _approvals, _workflow, new FixedLocalClock(Now), _employees);

    private static CreatePunchRequestCommand Command(DateTime? occurred = null, DateOnly? workDate = null) =>
        new(workDate ?? WorkDate, occurred ?? Occurred, PunchDirection.In, "Kart okutmayı unuttum");

    private PunchRequest NewPending()
    {
        var request = PunchRequest.Create(_tenantId, _employeeId, WorkDate, Occurred, PunchDirection.In, "unuttum").Value;
        request.AttachApproval(Guid.NewGuid());
        return request;
    }

    [Fact]
    public async Task Create_starts_workflow_with_AttendancePunch_type()
    {
        var result = await CreateHandler().Handle(Command(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(PunchRequestStatus.Pending);
        _requests.Received(1).Add(Arg.Is<PunchRequest>(r => r.EmployeeId == _employeeId && r.ApprovalRequestId != null));
        await _workflow.Received(1).StartAsync(
            "AttendancePunch", result.Value.Id, _employeeId, null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cannot_request_future_time()
    {
        var result = await CreateHandler().Handle(Command(Now.AddHours(2), DateOnly.FromDateTime(Now)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.PunchInFuture);
        await _workflow.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Cannot_request_in_locked_month()
    {
        _approvals.FindAsync(_employeeId, new DateOnly(2026, 9, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, new DateOnly(2026, 9, 1), Now, Guid.NewGuid()));

        var result = await CreateHandler().Handle(Command(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.PunchRequestPeriodLocked);
        _requests.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Duplicate_pending_same_time_rejected()
    {
        _requests.ListForEmployeeAsync(_employeeId, WorkDate, WorkDate, Arg.Any<CancellationToken>()).Returns([NewPending()]);

        var result = await CreateHandler().Handle(Command(), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.PunchRequestDuplicate);
    }

    [Fact]
    public async Task Cancel_only_when_pending()
    {
        var pending = NewPending();
        var decided = NewPending();
        decided.Reject(Now);
        _requests.GetByIdAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(pending);
        _requests.GetByIdAsync(decided.Id, Arg.Any<CancellationToken>()).Returns(decided);
        _workflow.CancelAsync(default, default, default).ReturnsForAnyArgs(Result.Success());
        var handler = new CancelPunchRequestHandler(_currentUser, _requests, _workflow, _clock);

        var ok = await handler.Handle(new CancelPunchRequestCommand(pending.Id), TestContext.Current.CancellationToken);
        var bad = await handler.Handle(new CancelPunchRequestCommand(decided.Id), TestContext.Current.CancellationToken);

        ok.IsSuccess.ShouldBeTrue();
        pending.Status.ShouldBe(PunchRequestStatus.Cancelled);
        bad.IsFailure.ShouldBeTrue();
        decided.Status.ShouldBe(PunchRequestStatus.Rejected);
    }

    [Fact]
    public async Task Approved_creates_punch_with_Request_source_and_recalculates()
    {
        var request = NewPending();
        var punchId = Guid.NewGuid();
        _requests.FindByApprovalRequestIdAsync(request.ApprovalRequestId!.Value, Arg.Any<CancellationToken>()).Returns(request);
        _recorder.RecordAsync(default!, default).ReturnsForAnyArgs(Result.Success(punchId));
        var handler = new PunchRequestApprovalCompletedHandler(_requests, _recorder, _recalculator, _unitOfWork, _clock);

        await handler.Handle(
            new ApprovalCompleted(_tenantId, request.ApprovalRequestId.Value, "AttendancePunch", request.Id, _employeeId), TestContext.Current.CancellationToken);

        await _recorder.Received(1).RecordAsync(
            Arg.Is<PunchInput>(p => p.Source == PunchSourceDto.Request && p.SourceRef == request.Id.ToString() && p.EmployeeId == _employeeId
                && p.OccurredAtUtc == Occurred && p.Direction == PunchDirectionDto.In && p.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
        await _recalculator.Received(1).RecalculateNowAsync(
            _tenantId, WorkDate, WorkDate, Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Single() == _employeeId), Arg.Any<CancellationToken>());
        request.Status.ShouldBe(PunchRequestStatus.Approved);
        request.CreatedPunchId.ShouldBe(punchId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approved_ignores_other_request_types()
    {
        var handler = new PunchRequestApprovalCompletedHandler(_requests, _recorder, _recalculator, _unitOfWork, _clock);

        await handler.Handle(new ApprovalCompleted(_tenantId, Guid.NewGuid(), "Leave", Guid.NewGuid(), _employeeId), TestContext.Current.CancellationToken);

        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task Rejected_sets_status_rejected_no_punch()
    {
        var request = NewPending();
        _requests.FindByApprovalRequestIdAsync(request.ApprovalRequestId!.Value, Arg.Any<CancellationToken>()).Returns(request);
        var handler = new PunchRequestApprovalRejectedHandler(_requests, _unitOfWork, _clock);

        await handler.Handle(
            new ApprovalRejected(_tenantId, request.ApprovalRequestId.Value, "AttendancePunch", request.Id, _employeeId, 1), TestContext.Current.CancellationToken);

        request.Status.ShouldBe(PunchRequestStatus.Rejected);
        request.CreatedPunchId.ShouldBeNull();
        await _recorder.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task Summary_provider_projects_the_requested_punch()
    {
        var request = NewPending();
        _requests.GetByIdAsync(request.Id, Arg.Any<CancellationToken>()).Returns(request);
        var provider = new PunchRequestApprovalSummaryProvider(_requests);

        var summary = await provider.GetSummaryAsync(_tenantId, request.Id, TestContext.Current.CancellationToken);

        provider.RequestType.ShouldBe("AttendancePunch");
        summary!.RequestDate.ShouldBe(WorkDate);
        summary.AttendancePunch!.Direction.ShouldBe("in");
        summary.AttendancePunch.OccurredAtUtc.ShouldBe(Occurred);
    }

    [Fact]
    public async Task Manager_list_filters_by_status()
    {
        var pending = NewPending();
        _requests.ListAsync(PunchRequestStatus.Pending, Arg.Any<CancellationToken>()).Returns([pending]);
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.punchrequest.manage", Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);
        var handler = new ListPunchRequestsHandler(_tenant, _dataScope, _requests, _employees);

        var result = await handler.Handle(new ListPunchRequestsQuery(PunchRequestStatus.Pending), TestContext.Current.CancellationToken);

        result.Value.Select(r => r.Id).ShouldBe([pending.Id]);
        await _requests.Received(1).ListAsync(PunchRequestStatus.Pending, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manager_list_hides_employees_outside_data_scope()
    {
        _requests.ListAsync(null, Arg.Any<CancellationToken>()).Returns([NewPending()]);
        _dataScope.GetVisibleEmployeeIdsAsync("attendance.punchrequest.manage", Arg.Any<CancellationToken>())
            .Returns((IReadOnlySet<Guid>?)new HashSet<Guid> { Guid.NewGuid() });
        var handler = new ListPunchRequestsHandler(_tenant, _dataScope, _requests, _employees);

        var result = await handler.Handle(new ListPunchRequestsQuery(null), TestContext.Current.CancellationToken);

        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rows_include_employee_name_and_work_date()
    {
        var pending = NewPending();
        _requests.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([pending]);
        var handler = new ListMyPunchRequestsHandler(_tenant, _currentUser, _requests, _employees);

        var result = await handler.Handle(new ListMyPunchRequestsQuery(), TestContext.Current.CancellationToken);

        var row = result.Value.Single();
        row.EmployeeName.ShouldBe("Ayşe Yılmaz");
        row.WorkDate.ShouldBe(WorkDate);
        row.ApprovalRequestId.ShouldBe(pending.ApprovalRequestId);
    }

    private sealed class FixedTimeProvider(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }
}
