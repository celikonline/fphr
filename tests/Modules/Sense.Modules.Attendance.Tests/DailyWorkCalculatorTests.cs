using Sense.Modules.Attendance.Domain.AttendanceDays;
using Shouldly;
using Xunit;

namespace Sense.Modules.Attendance.Tests;

public sealed class DailyWorkCalculatorTests
{
    private static readonly ShiftLimits NightLimits = new(11 * 60, true, new TimeOnly(20, 0), new TimeOnly(6, 0), 450);

    [Fact]
    public void Calculate_NoCheckInOrOut_ReturnsEmpty()
    {
        var result = DailyWorkCalculator.Calculate(new DailyWorkInput(null, null, false, 0, 480, ShiftLimits.None));

        result.ShouldBe(DailyWorkResult.Empty);
    }

    // m.68 boundary: exactly 4h gross → still below the 30dk bracket (< 4h is the "no break" zone; the bracket starts AT 4h).
    [Theory]
    [InlineData(3, 59, 0)]
    [InlineData(4, 0, 30)]
    [InlineData(7, 30, 30)]
    [InlineData(7, 31, 60)]
    public void LegalMinimumBreakMinutes_HonorsM68Thresholds(int hours, int minutes, int expectedBreak)
    {
        DailyWorkCalculator.LegalMinimumBreakMinutes((hours * 60) + minutes).ShouldBe(expectedBreak);
    }

    [Fact]
    public void Calculate_ExactlyFourHours_Applies30MinuteBreak()
    {
        var input = new DailyWorkInput(new TimeOnly(9, 0), new TimeOnly(13, 0), false, 0, 210, ShiftLimits.None);

        var result = DailyWorkCalculator.Calculate(input);

        result.BreakMinutesApplied.ShouldBe(30);
        result.NetWorkedMinutes.ShouldBe((4 * 60) - 30);
    }

    [Fact]
    public void Calculate_MoreThanSevenAndHalfHours_AppliesOneHourBreak()
    {
        var input = new DailyWorkInput(new TimeOnly(8, 0), new TimeOnly(16, 0), false, 0, 450, ShiftLimits.None);

        var result = DailyWorkCalculator.Calculate(input);

        result.BreakMinutesApplied.ShouldBe(60);
        result.NetWorkedMinutes.ShouldBe((8 * 60) - 60);
    }

    [Fact]
    public void Calculate_ObservedBreakLongerThanLegalMinimum_UsesObservedBreak()
    {
        var input = new DailyWorkInput(new TimeOnly(8, 0), new TimeOnly(17, 0), false, 90, 450, ShiftLimits.None);

        var result = DailyWorkCalculator.Calculate(input);

        result.BreakMinutesApplied.ShouldBe(90);
    }

    [Fact]
    public void Calculate_NetWorkedOverElevenHours_FlagsDailyLimitExceeded()
    {
        // 07:00-19:00 = 12s brüt, 1sa yasal mola → 11s net: tam sınırda, aşmamalı.
        var exact = DailyWorkCalculator.Calculate(new DailyWorkInput(new TimeOnly(7, 0), new TimeOnly(19, 0), false, 0, 600, ShiftLimits.None));
        exact.NetWorkedMinutes.ShouldBe(11 * 60);
        exact.DailyLimitExceeded.ShouldBeFalse();

        // Bir dakika daha: 11s01dk net → sınırı aşar.
        var over = DailyWorkCalculator.Calculate(new DailyWorkInput(new TimeOnly(7, 0), new TimeOnly(19, 1), false, 0, 600, ShiftLimits.None));
        over.NetWorkedMinutes.ShouldBe((11 * 60) + 1);
        over.DailyLimitExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Calculate_NightWindowWrapsMidnight_ComputesOverlapAcrossBothDays()
    {
        // 20:30-05:40 (overnight), tamamı gece penceresi (20:00-06:00) içinde: 9s10dk brüt, 1sa asgari mola sonrası
        // 8s10dk (490dk) net - hepsi gece sayılır (pencerenin tamamını kapsıyor) ve m.69'un 7,5 saatini aşar.
        var input = new DailyWorkInput(new TimeOnly(20, 30), new TimeOnly(5, 40), true, 0, 480, NightLimits);

        var result = DailyWorkCalculator.Calculate(input);

        result.NetWorkedMinutes.ShouldBe(490);
        result.NightMinutes.ShouldBe(490);
        result.NightLimitExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Calculate_DayShiftEntirelyOutsideNightWindow_HasZeroNightMinutes()
    {
        var input = new DailyWorkInput(new TimeOnly(9, 0), new TimeOnly(17, 0), false, 0, 480, NightLimits);

        var result = DailyWorkCalculator.Calculate(input);

        result.NightMinutes.ShouldBe(0);
        result.NightLimitExceeded.ShouldBeFalse();
    }

    [Fact]
    public void Calculate_NightMinutesAtExactly450_DoesNotExceedLimit()
    {
        // 21:00-05:30 (overnight): 8,5 saat (510dk) brüt, tamamı gece penceresi (20:00-06:00) içinde; m.68 asgari mola
        // 1 saat (>7,5s brüt) düşülünce net tam 450dk (7,5s) - m.69 sınırında, henüz AŞMIYOR.
        var input = new DailyWorkInput(new TimeOnly(21, 0), new TimeOnly(5, 30), true, 0, 480, NightLimits);

        var result = DailyWorkCalculator.Calculate(input);

        result.NetWorkedMinutes.ShouldBe(450);
        result.NightMinutes.ShouldBe(450);
        result.NightLimitExceeded.ShouldBeFalse();
    }

    [Fact]
    public void Calculate_NightMinutesJustOver450_ExceedsLimit()
    {
        // Bir önceki testten bir dakika daha uzun (21:00-05:31): net 451dk - m.69'u aşar.
        var input = new DailyWorkInput(new TimeOnly(21, 0), new TimeOnly(5, 31), true, 0, 480, NightLimits);

        var result = DailyWorkCalculator.Calculate(input);

        result.NetWorkedMinutes.ShouldBe(451);
        result.NightMinutes.ShouldBe(451);
        result.NightLimitExceeded.ShouldBeTrue();
    }

    [Fact]
    public void Calculate_OvertimeMinutes_IsWorkedMinusPlannedFlooredAtZero()
    {
        var over = DailyWorkCalculator.Calculate(new DailyWorkInput(new TimeOnly(9, 0), new TimeOnly(19, 0), false, 0, 480, ShiftLimits.None));
        over.OvertimeMinutes.ShouldBe(over.NetWorkedMinutes - 480);

        var under = DailyWorkCalculator.Calculate(new DailyWorkInput(new TimeOnly(9, 0), new TimeOnly(16, 0), false, 0, 480, ShiftLimits.None));
        under.OvertimeMinutes.ShouldBe(0);
    }
}
