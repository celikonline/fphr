using Sense.Modules.Attendance.Domain.AttendanceDays;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

/// <summary>Kart senseik-014 §2: <see cref="AttendanceDay.Recompute"/> delegates the mola/gece/limit fields to <see cref="DailyWorkCalculator"/>.</summary>
public sealed class AttendanceDayDailyWorkTests
{
    private static readonly TimeOnly PlannedStart = new(9, 0);
    private static readonly TimeOnly PlannedEnd = new(17, 0);
    private const int PlannedMinutes = 8 * 60;

    [Fact]
    public void Record_WithActualBreakMinutes_AppliesLegalMinimumWhenHigher()
    {
        // 08:00-17:00 = 9s brüt (legal min 1sa), gözlemlenen mola yalnız 20dk.
        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), new TimeOnly(8, 0), new TimeOnly(17, 0),
            PlannedStart, PlannedEnd, PlannedMinutes, breakMinutes: 0, AttendanceDaySource.Device, overnight: false, actualBreakMinutes: 20);

        day.ActualBreakMinutes.ShouldBe(20);
        day.BreakMinutesApplied.ShouldBe(60);
        day.NetWorkedMinutes.ShouldBe((9 * 60) - 60);
    }

    [Fact]
    public void Record_WithoutShiftLimits_StillEnforcesDefaultElevenHourLimit()
    {
        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), new TimeOnly(7, 0), new TimeOnly(19, 1),
            PlannedStart, PlannedEnd, PlannedMinutes, breakMinutes: 0, AttendanceDaySource.Manual);

        day.DailyLimitExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Record_WithShiftLimitsNightWindow_FlagsNightMinutesAndLimit()
    {
        var limits = new ShiftLimits(11 * 60, NightWorkEnabled: true, new TimeOnly(20, 0), new TimeOnly(6, 0), NightWorkMaxMinutes: 60);

        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), new TimeOnly(21, 0), new TimeOnly(23, 0),
            PlannedStart, PlannedEnd, PlannedMinutes, breakMinutes: 0, AttendanceDaySource.Manual, overnight: false, actualBreakMinutes: 0, limits);

        day.NightMinutes.ShouldBeGreaterThan(0);
        day.NightLimitExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Record_OvertimeMinutes_MirrorsNetWorkedMinusPlanned()
    {
        var day = AttendanceDay.Record(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 21), PlannedStart, new TimeOnly(19, 0),
            PlannedStart, PlannedEnd, PlannedMinutes, breakMinutes: 0, AttendanceDaySource.Manual);

        day.OvertimeMinutes.ShouldBe(day.NetWorkedMinutes - PlannedMinutes);
        day.OvertimeMinutes.ShouldBeGreaterThan(0);
    }
}
