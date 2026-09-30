using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.Daily;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.MonthlyTimesheet;
using Sense.Modules.Attendance.Domain.Punches;
using Sense.Modules.Identity.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests.Daily;

/// <summary>Manual punch add/delete (spec 5.4 + K6): reason, permission/scope, locked month, audit trail via the recorder.</summary>
public sealed class DailyPunchCommandsTests
{
    private static readonly DateTime When = new(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly IDataScopeService _dataScope = Substitute.For<IDataScopeService>();
    private readonly IPunchRecorder _recorder = Substitute.For<IPunchRecorder>();
    private readonly IPunchRepository _punches = Substitute.For<IPunchRepository>();
    private readonly IMonthlyTimesheetApprovalRepository _approvals = Substitute.For<IMonthlyTimesheetApprovalRepository>();
    private readonly ITenantTimeZoneLookup _zones = Substitute.For<ITenantTimeZoneLookup>();

    public DailyPunchCommandsTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _user.UserId.Returns(_userId);
        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.PunchManageKey, _employeeId, Arg.Any<CancellationToken>()).Returns(true);
        _recorder.RecordAsync(Arg.Any<PunchInput>(), Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.NewGuid()));
        _recorder.SoftDeleteAsync(_tenantId, Arg.Any<Guid>(), _userId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private AddDailyPunchHandler AddHandler() => new(_tenant, _user, _dataScope, _recorder, _approvals, _zones);

    private DeleteDailyPunchHandler DeleteHandler() => new(_tenant, _user, _dataScope, _recorder, _punches, _approvals);

    private static AddDailyPunchCommand Add(Guid employeeId, string? reason) => new(employeeId, When, PunchDirectionDto.In, reason!);

    [Fact]
    public async Task Manual_add_requires_reason_and_permission()
    {
        var noReason = await AddHandler().Handle(Add(_employeeId, "  "), TestContext.Current.CancellationToken);
        noReason.IsFailure.ShouldBeTrue();
        noReason.Error.Code.ShouldBe(AttendanceErrors.PunchReasonRequired);

        var outOfScope = await AddHandler().Handle(Add(Guid.NewGuid(), "forgot"), TestContext.Current.CancellationToken);
        outOfScope.Error.Type.ShouldBe(ErrorType.Forbidden);
        await _recorder.DidNotReceive().RecordAsync(Arg.Any<PunchInput>(), Arg.Any<CancellationToken>());

        typeof(AddDailyPunchCommand).GetCustomAttributes(typeof(RequiresPermissionAttribute), false)
            .Cast<RequiresPermissionAttribute>().ShouldContain(a => a.Permission == AttendancePermissions.PunchManageKey);
    }

    [Fact]
    public async Task Manual_add_records_a_manual_punch_through_the_recorder()
    {
        var result = await AddHandler().Handle(Add(_employeeId, "forgot to badge"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _recorder.Received(1).RecordAsync(
            Arg.Is<PunchInput>(p => p.EmployeeId == _employeeId && p.IsManual && p.Source == PunchSourceDto.Manual && p.CreatedByUserId == _userId
                && p.Reason == "forgot to badge" && p.OccurredAtUtc == When && p.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manual_add_in_locked_month_fails()
    {
        _approvals.FindAsync(_employeeId, new DateOnly(2026, 9, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, new DateOnly(2026, 9, 1), When, _userId));

        var result = await AddHandler().Handle(Add(_employeeId, "late fix"), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.PeriodLocked);
        await _recorder.DidNotReceive().RecordAsync(Arg.Any<PunchInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_writes_audit_entry()
    {
        var punch = Punch.Create(_tenantId, _employeeId, When, new DateOnly(2026, 9, 21), Sense.Modules.Attendance.Domain.SelfCheckIns.PunchDirection.In,
            PunchSource.Manual, null, null, null, null, null, true, _userId, "orig", When.AddHours(1)).Value;
        _punches.FindAsync(_tenantId, punch.Id, Arg.Any<CancellationToken>()).Returns(punch);

        var result = await DeleteHandler().Handle(new DeleteDailyPunchCommand(punch.Id, "duplicate"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _recorder.Received(1).SoftDeleteAsync(_tenantId, punch.Id, _userId, "duplicate", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_requires_reason_scope_and_unlocked_month()
    {
        var punch = Punch.Create(_tenantId, _employeeId, When, new DateOnly(2026, 9, 21), Sense.Modules.Attendance.Domain.SelfCheckIns.PunchDirection.In,
            PunchSource.Manual, null, null, null, null, null, true, _userId, "orig", When.AddHours(1)).Value;
        _punches.FindAsync(_tenantId, punch.Id, Arg.Any<CancellationToken>()).Returns(punch);

        (await DeleteHandler().Handle(new DeleteDailyPunchCommand(punch.Id, " "), TestContext.Current.CancellationToken))
            .Error.Code.ShouldBe(AttendanceErrors.PunchReasonRequired);

        (await DeleteHandler().Handle(new DeleteDailyPunchCommand(Guid.NewGuid(), "x"), TestContext.Current.CancellationToken))
            .Error.Type.ShouldBe(ErrorType.NotFound);

        _approvals.FindAsync(_employeeId, new DateOnly(2026, 9, 1), Arg.Any<CancellationToken>())
            .Returns(MonthlyTimesheetApproval.Approve(_tenantId, _employeeId, new DateOnly(2026, 9, 1), When, _userId));
        (await DeleteHandler().Handle(new DeleteDailyPunchCommand(punch.Id, "x"), TestContext.Current.CancellationToken))
            .Error.Code.ShouldBe(AttendanceErrors.PeriodLocked);

        _dataScope.CanAccessEmployeeAsync(AttendancePermissions.PunchManageKey, _employeeId, Arg.Any<CancellationToken>()).Returns(false);
        (await DeleteHandler().Handle(new DeleteDailyPunchCommand(punch.Id, "x"), TestContext.Current.CancellationToken))
            .Error.Type.ShouldBe(ErrorType.Forbidden);
        await _recorder.DidNotReceive().SoftDeleteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
