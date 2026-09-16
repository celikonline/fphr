using NSubstitute;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class TimesheetWeekFeaturesTests
{
    private static readonly TimeOnly PlannedStart = new(9, 0);
    private static readonly TimeOnly PlannedEnd = new(17, 0);
    private const int PlannedMinutes = 8 * 60;

    // 2026-09-21 is a Monday; the ISO week runs through 2026-09-27 (Sunday).
    private static readonly DateOnly Monday = new(2026, 9, 21);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly IAttendanceDayRepository _days = Substitute.For<IAttendanceDayRepository>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();

    private AttendanceDay Resolved(DateOnly date)
    {
        var day = AttendanceDay.Record(Guid.NewGuid(), _employeeId, date, PlannedStart, PlannedEnd, PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Manual);
        return day;
    }

    private AttendanceDay Unresolved(DateOnly date) =>
        AttendanceDay.Record(Guid.NewGuid(), _employeeId, date, PlannedStart, new TimeOnly(19, 0), PlannedStart, PlannedEnd, PlannedMinutes, 0, AttendanceDaySource.Manual);

    private GetTimesheetWeekSummaryHandler CreateHandler() => new(_days, _schedules);

    [Fact]
    public async Task Handle_NoRecordsYet_ReturnsOpen()
    {
        _schedules.GetWorkingDaysAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>()).Returns(new List<DateOnly> { Monday, Tuesday });
        _days.ListForEmployeeAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());

        var result = await CreateHandler().Handle(new GetTimesheetWeekSummaryQuery(_employeeId, Monday), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(TimesheetWeekStatus.Open);
        result.Value.WorkingDayCount.ShouldBe(2);
        result.Value.RecordedWorkingDayCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_UnresolvedDayInRange_ReturnsHasIssues()
    {
        _schedules.GetWorkingDaysAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>()).Returns(new List<DateOnly> { Monday, Tuesday });
        _days.ListForEmployeeAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { Resolved(Monday), Unresolved(Tuesday) });

        var result = await CreateHandler().Handle(new GetTimesheetWeekSummaryQuery(_employeeId, Monday), TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(TimesheetWeekStatus.HasIssues);
        result.Value.UnresolvedDayCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_AllWorkingDaysRecordedAndResolved_ReturnsCompleted()
    {
        _schedules.GetWorkingDaysAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>()).Returns(new List<DateOnly> { Monday, Tuesday });
        _days.ListForEmployeeAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { Resolved(Monday), Resolved(Tuesday) });

        var result = await CreateHandler().Handle(new GetTimesheetWeekSummaryQuery(_employeeId, Monday), TestContext.Current.CancellationToken);

        result.Value.Status.ShouldBe(TimesheetWeekStatus.Completed);
        result.Value.WeekStart.ShouldBe(Monday);
        result.Value.WeekEnd.ShouldBe(Monday.AddDays(6));
    }

    [Fact]
    public async Task Handle_MidWeekDateNormalizesToMonday()
    {
        _schedules.GetWorkingDaysAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>()).Returns(new List<DateOnly>());
        _days.ListForEmployeeAsync(_employeeId, Monday, Monday.AddDays(6), Arg.Any<CancellationToken>()).Returns(new List<AttendanceDay>());

        var result = await CreateHandler().Handle(new GetTimesheetWeekSummaryQuery(_employeeId, Tuesday), TestContext.Current.CancellationToken);

        result.Value.WeekStart.ShouldBe(Monday);
        result.Value.Status.ShouldBe(TimesheetWeekStatus.Completed);
    }
}
