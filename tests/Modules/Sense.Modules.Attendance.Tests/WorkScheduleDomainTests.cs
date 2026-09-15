using Sense.Modules.Attendance.Domain;
using Sense.Modules.Attendance.Domain.WorkSchedules;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class WorkScheduleDomainTests
{
    private static List<WorkScheduleDay> MonToFri(TimeOnly start, TimeOnly end) =>
        Enum.GetValues<DayOfWeek>()
            .Select(d => d is DayOfWeek.Saturday or DayOfWeek.Sunday ? WorkScheduleDay.Off(d) : WorkScheduleDay.Working(d, start, end))
            .ToList();

    [Fact]
    public void CreateDefault_IsGeneralWorkTable_MondayToFriday_09to18_WithBreak()
    {
        var schedule = WorkSchedule.CreateDefault(Guid.NewGuid(), "seed-key");

        schedule.IsDefault.ShouldBeTrue();
        schedule.IncludeSaturdayWhenFullWeek.ShouldBeTrue();
        schedule.GetDay(DayOfWeek.Monday).IsWorkingDay.ShouldBeTrue();
        schedule.GetDay(DayOfWeek.Saturday).IsWorkingDay.ShouldBeFalse();
        schedule.WeeklyMinutes.ShouldBe(5 * 8 * 60);
    }

    [Fact]
    public void Create_FailsWhenNotAllSevenDaysOfWeekAreProvided()
    {
        var days = MonToFri(new TimeOnly(9, 0), new TimeOnly(18, 0)).Take(6).ToList();

        var result = WorkSchedule.Create(Guid.NewGuid(), "Eksik", days);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ScheduleDaysCountInvalid);
    }

    [Fact]
    public void Create_FailsWhenAWorkingDayHasNoTimes()
    {
        var days = MonToFri(new TimeOnly(9, 0), new TimeOnly(18, 0));
        days[0] = WorkScheduleDay.Working(days[0].DayOfWeek, new TimeOnly(18, 0), new TimeOnly(9, 0));

        var result = WorkSchedule.Create(Guid.NewGuid(), "Ters saat", days);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ScheduleDayRangeInvalid);
    }

    [Fact]
    public void Create_FailsWhenBreakIsOutsideWorkingHours()
    {
        var days = MonToFri(new TimeOnly(9, 0), new TimeOnly(18, 0));
        days[0] = WorkScheduleDay.Working(days[0].DayOfWeek, new TimeOnly(9, 0), new TimeOnly(18, 0), new TimeOnly(19, 0), new TimeOnly(20, 0));

        var result = WorkSchedule.Create(Guid.NewGuid(), "Kötü mola", days);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AttendanceDomainErrors.ScheduleDayBreakInvalid);
    }

    [Fact]
    public void MakeDefault_ClearsDefaultOnEveryOtherScheduleOfTheTenant()
    {
        var tenantId = Guid.NewGuid();
        var a = WorkSchedule.CreateDefault(tenantId, "a");
        var b = WorkSchedule.Create(tenantId, "b", MonToFri(new TimeOnly(9, 0), new TimeOnly(17, 0))).Value;

        b.MakeDefault([a, b]);

        a.IsDefault.ShouldBeFalse();
        b.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void EnsureDeletable_RejectsTheDefaultSchedule_AndSchedulesWithAssignedEmployees()
    {
        var tenantId = Guid.NewGuid();
        var a = WorkSchedule.CreateDefault(tenantId, "seed");
        var b = WorkSchedule.Create(tenantId, "b", MonToFri(new TimeOnly(9, 0), new TimeOnly(17, 0))).Value;
        b.MakeDefault([a, b]);

        a.IsDefault.ShouldBeFalse();
        a.EnsureDeletable(3).Error.Code.ShouldBe(AttendanceDomainErrors.ScheduleHasEmployees);
        a.EnsureDeletable(0).IsSuccess.ShouldBeTrue();
        b.EnsureDeletable(0).Error.Code.ShouldBe(AttendanceDomainErrors.ScheduleIsDefault);
    }
}
