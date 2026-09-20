using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Pure unit tests for <see cref="HolidayPayCalculator"/> (senseik-019 2d, İş K. md.46/47).</summary>
public sealed class HolidayPayCalculatorTests
{
    [Fact]
    public void DailyRate_DividesGrossByCalendarDays()
    {
        HolidayPayCalculator.DailyRate(300_000m, 30).ShouldBe(10_000m);
    }

    [Fact]
    public void Calculate_AddsOneDailyRatePerWorkedHolidayDay()
    {
        // Günlük ücret = 10,000. 2 hafta tatili + 1 resmî tatil günü çalışıldı = 3 ek yevmiye = 30,000.
        var pay = HolidayPayCalculator.Calculate(300_000m, calendarDaysInMonth: 30, weeklyRestDaysWorked: 2, publicHolidayDaysWorked: 1);

        pay.ShouldBe(30_000m);
    }

    [Fact]
    public void Calculate_NoWorkedHolidayDays_ReturnsZero()
    {
        HolidayPayCalculator.Calculate(300_000m, 30, 0, 0).ShouldBe(0m);
    }

    [Fact]
    public void Calculate_NegativeDays_Throws()
    {
        Should.Throw<ArgumentException>(() => HolidayPayCalculator.Calculate(300_000m, 30, -1, 0));
        Should.Throw<ArgumentException>(() => HolidayPayCalculator.Calculate(300_000m, 30, 0, -1));
    }
}
