using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.AttendanceDays;
using Sense.Modules.Attendance.Application.Shifts;
using Sense.Modules.Attendance.Contracts;
using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.AttendanceDays;
using Sense.Modules.Attendance.Domain.Shifts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>"Puantajım" özet kartları ve "Vardiyam" (package A5 self-servis queries).</summary>
public sealed class AttendanceSelfServiceFeaturesTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public AttendanceSelfServiceFeaturesTests() => _currentUser.EmployeeId.Returns(_employeeId);

    [Fact]
    public async Task GetMyTimesheetSummary_SumsWorkedMinutesAndCountsWorkedDays()
    {
        var days = Substitute.For<IAttendanceDayRepository>();
        var worked = AttendanceDay.Record(_tenantId, _employeeId, new DateOnly(2026, 9, 21), new TimeOnly(9, 0), new TimeOnly(17, 0), new TimeOnly(9, 0), new TimeOnly(17, 0), 480, 0, AttendanceDaySource.Manual);
        var dayOff = AttendanceDay.Record(_tenantId, _employeeId, new DateOnly(2026, 9, 20), null, null, null, null, 0, 0, AttendanceDaySource.Manual);
        days.ListForEmployeeAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<AttendanceDay> { worked, dayOff });
        var handler = new GetMyTimesheetSummaryHandler(days, _currentUser);

        var result = await handler.Handle(new GetMyTimesheetSummaryQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WorkedMinutes.ShouldBe(480);
        result.Value.WorkedDays.ShouldBe(1);
    }

    [Fact]
    public async Task GetMyTimesheetSummary_NoLinkedEmployee_ReturnsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);
        var handler = new GetMyTimesheetSummaryHandler(Substitute.For<IAttendanceDayRepository>(), _currentUser);

        var result = await handler.Handle(new GetMyTimesheetSummaryQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task GetMyShiftAssignments_ReturnsCallersShifts()
    {
        var shifts = Substitute.For<IShiftAssignmentRepository>();
        var assignment = ShiftAssignment.Create(_tenantId, _employeeId, new DateOnly(2026, 9, 21), new TimeOnly(9, 0), new TimeOnly(17, 0), 0, isNight: false, note: null).Value;
        assignment.Publish(DateTime.UtcNow);
        shifts.ListForRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_employeeId)), Arg.Any<CancellationToken>())
            .Returns(new List<ShiftAssignment> { assignment });
        var handler = new GetMyShiftAssignmentsHandler(shifts, _currentUser);

        var result = await handler.Handle(new GetMyShiftAssignmentsQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetMyShiftAssignments_NoLinkedEmployee_ReturnsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);
        var handler = new GetMyShiftAssignmentsHandler(Substitute.For<IShiftAssignmentRepository>(), _currentUser);

        var result = await handler.Handle(new GetMyShiftAssignmentsQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }
}
