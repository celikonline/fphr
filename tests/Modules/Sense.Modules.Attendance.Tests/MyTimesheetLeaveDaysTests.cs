using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Leave.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>GET attendance/timesheet/me (mobil Puantaj): approved leave days are marked onLeave (+ unmasked type name), and leave-only rows are opt-in.</summary>
public sealed class MyTimesheetLeaveDaysTests
{
    // 2026-09-21 is a Monday; 2026-09-26 a Saturday.
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Saturday = Monday.AddDays(5);
    private static readonly DateOnly From = Monday;
    private static readonly DateOnly To = Monday.AddDays(6);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private readonly IApprovedLeaveDateLookup _leaveDates = Substitute.For<IApprovedLeaveDateLookup>();

    public MyTimesheetLeaveDaysTests()
    {
        _tenant.TenantId.Returns(_tenantId);
        _currentUser.EmployeeId.Returns(_employeeId);
        _days.ListForEmployeeAsync(_employeeId, From, To, Arg.Any<CancellationToken>()).Returns([]);
        _schedules.GetWorkingDaysAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => WeekdaysBetween(call.ArgAt<DateOnly>(1), call.ArgAt<DateOnly>(2)));
    }

    private static IReadOnlyList<DateOnly> WeekdaysBetween(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .ToList();

    private void GivenLeave(params ApprovedLeaveDayDetail[] leave) =>
        _leaveDates.GetApprovedLeaveDaysWithTypeAsync(_tenantId, _employeeId, From, To, Arg.Any<CancellationToken>()).Returns(leave);

    private AttendanceDay PunchedDay(DateOnly date) =>
        AttendanceDay.Record(_tenantId, _employeeId, date, new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 8 * 60, 0, AttendanceDaySource.Manual);

    private Task<Sense.Shared.Kernel.Results.Result<IReadOnlyList<AttendanceDayDto>>> Query(bool includeLeaveDays, IApprovedLeaveDateLookup? leaveDates) =>
        new GetMyAttendanceDaysHandler(_days, _currentUser, _tenant, _schedules, leaveDates)
            .Handle(new GetMyAttendanceDaysQuery(From, To, includeLeaveDays), TestContext.Current.CancellationToken);

    [Fact]
    public async Task LeaveOnlyWorkingDay_WithIncludeLeaveDays_AddsLeaveOnlyRow()
    {
        GivenLeave(new ApprovedLeaveDayDetail(Tuesday, "leave.type.annual"), new ApprovedLeaveDayDetail(Saturday, "leave.type.annual"));

        var result = await Query(includeLeaveDays: true, _leaveDates);

        result.IsSuccess.ShouldBeTrue();
        var row = result.Value.ShouldHaveSingleItem(); // Saturday is not a scheduled working day
        row.Id.ShouldBe(Guid.Empty);
        row.EmployeeId.ShouldBe(_employeeId);
        row.Date.ShouldBe(Tuesday);
        row.OnLeave.ShouldBeTrue();
        row.LeaveTypeName.ShouldBe("leave.type.annual");
        row.CheckIn.ShouldBeNull();
        row.CheckOut.ShouldBeNull();
        row.PlannedMinutes.ShouldBe(0);
        row.WorkedMinutes.ShouldBe(0);
        row.Anomalies.ShouldBe(AttendanceAnomaly.None);
        row.AllowedActions.ShouldBeEmpty();
    }

    [Fact]
    public async Task LeaveOnlyDay_WithoutIncludeLeaveDays_KeepsRecordedRowsOnly()
    {
        GivenLeave(new ApprovedLeaveDayDetail(Tuesday, "leave.type.annual"));

        var result = await Query(includeLeaveDays: false, _leaveDates);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WorkedOnLeaveDay_KeepsRecordAndMarksItOnLeave()
    {
        var worked = PunchedDay(Monday);
        worked.ApplyLeaveOverlap(true);
        var plain = PunchedDay(Tuesday);
        _days.ListForEmployeeAsync(_employeeId, From, To, Arg.Any<CancellationToken>()).Returns([worked, plain]);
        GivenLeave(new ApprovedLeaveDayDetail(Monday, "leave.type.annual"));

        var result = await Query(includeLeaveDays: true, _leaveDates);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        var onLeave = result.Value[0];
        onLeave.Id.ShouldBe(worked.Id);
        onLeave.OnLeave.ShouldBeTrue();
        onLeave.LeaveTypeName.ShouldBe("leave.type.annual");
        onLeave.CheckIn.ShouldBe(new TimeOnly(9, 0));
        onLeave.Anomalies.HasFlag(AttendanceAnomaly.WorkedWhileOnLeave).ShouldBeTrue();
        result.Value[1].Id.ShouldBe(plain.Id);
        result.Value[1].OnLeave.ShouldBeFalse();
        result.Value[1].LeaveTypeName.ShouldBeNull();
    }

    [Fact]
    public async Task MaskedLeaveType_IsOnLeaveWithoutName()
    {
        _days.ListForEmployeeAsync(_employeeId, From, To, Arg.Any<CancellationToken>()).Returns([PunchedDay(Monday)]);
        GivenLeave(new ApprovedLeaveDayDetail(Monday, null), new ApprovedLeaveDayDetail(Tuesday, null));

        var result = await Query(includeLeaveDays: true, _leaveDates);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(r => r.Date).ShouldBe([Monday, Tuesday]);
        result.Value.ShouldAllBe(r => r.OnLeave && r.LeaveTypeName == null);
    }

    [Fact]
    public async Task NoLeaveModuleInHost_ReturnsRecordedRowsWithoutLeaveInfo()
    {
        _days.ListForEmployeeAsync(_employeeId, From, To, Arg.Any<CancellationToken>()).Returns([PunchedDay(Monday)]);

        var result = await Query(includeLeaveDays: true, leaveDates: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().OnLeave.ShouldBeFalse();
    }
}
