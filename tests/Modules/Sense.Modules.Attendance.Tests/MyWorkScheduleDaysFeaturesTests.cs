using NSubstitute;
using Sense.Modules.Attendance.Application;
using Sense.Modules.Attendance.Application.WorkSchedules;
using Sense.Modules.Attendance.Contracts;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Unit tests for "Takvimim" self uçu (FG-020 kabul kriteri 1, Karar 14): sabit çalışma programının gün gün dökümü.</summary>
public sealed class MyWorkScheduleDaysFeaturesTests
{
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IWorkScheduleReadService _schedules = Substitute.For<IWorkScheduleReadService>();
    private static readonly DateOnly Monday = new(2026, 9, 21);

    public MyWorkScheduleDaysFeaturesTests() => _currentUser.EmployeeId.Returns(_employeeId);

    private GetMyWorkScheduleDaysHandler CreateHandler() => new(_currentUser, _schedules);

    [Fact]
    public async Task NoLinkedEmployee_ReturnsNotFound()
    {
        _currentUser.EmployeeId.Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetMyWorkScheduleDaysQuery(Monday, Monday), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.EmployeeNotFound);
    }

    [Fact]
    public async Task FromAfterTo_ReturnsDateRangeInvalid()
    {
        var result = await CreateHandler().Handle(new GetMyWorkScheduleDaysQuery(Monday, Monday.AddDays(-1)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.DateRangeInvalid);
    }

    [Fact]
    public async Task RangeOfExactly42Days_Succeeds()
    {
        _schedules.GetForDateAsync(_employeeId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(false, null, null, null, null, false));

        var result = await CreateHandler().Handle(new GetMyWorkScheduleDaysQuery(Monday, Monday.AddDays(41)), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(42);
    }

    [Fact]
    public async Task RangeOver42Days_ReturnsRangeTooLong()
    {
        var result = await CreateHandler().Handle(new GetMyWorkScheduleDaysQuery(Monday, Monday.AddDays(42)), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceErrors.WorkScheduleRangeTooLong);
        result.Error.Args!["max"].ShouldBe(GetMyWorkScheduleDaysHandler.MaxRangeDays);
    }

    [Fact]
    public async Task WorkingDayWithBreak_ComputesBreakMinutes()
    {
        _schedules.GetForDateAsync(_employeeId, Monday, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(true, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(12, 30), new TimeOnly(13, 30), true));

        var result = await CreateHandler().Handle(new GetMyWorkScheduleDaysQuery(Monday, Monday), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var day = result.Value.ShouldHaveSingleItem();
        day.Date.ShouldBe(Monday);
        day.IsWorkingDay.ShouldBeTrue();
        day.Start.ShouldBe(new TimeOnly(9, 0));
        day.End.ShouldBe(new TimeOnly(18, 0));
        day.BreakMinutes.ShouldBe(60);
    }

    [Fact]
    public async Task NonWorkingDay_HasNoStartEndOrBreak()
    {
        _schedules.GetForDateAsync(_employeeId, Monday, Arg.Any<CancellationToken>())
            .Returns(new EmployeeScheduleDay(false, null, null, null, null, false));

        var result = await CreateHandler().Handle(new GetMyWorkScheduleDaysQuery(Monday, Monday), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var day = result.Value.ShouldHaveSingleItem();
        day.IsWorkingDay.ShouldBeFalse();
        day.Start.ShouldBeNull();
        day.End.ShouldBeNull();
        day.BreakMinutes.ShouldBe(0);
    }
}
