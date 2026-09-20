using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Pure unit tests for <see cref="OvertimePayCalculator"/> (senseik-019 2d, İş K. md.41).</summary>
public sealed class OvertimePayCalculatorTests
{
    [Fact]
    public void HourlyRate_DividesGrossByMonthlyDivisor()
    {
        // 225 saatlik esas (30 gün * 7,5 saat, md.63) üzerinden: 225,000 / 225 = 1,000 TL/saat, yuvarlamasız net.
        OvertimePayCalculator.HourlyRate(225_000m).ShouldBe(1_000m);
    }

    [Fact]
    public void Calculate_AppliesTwentyFivePercentAndFiftyPercentRatesSeparately()
    {
        // Saatlik ücret = 1,000. 10 saat %25 zamlı = 10*1,000*1.25 = 12,500. 4 saat %50 zamlı = 4*1,000*1.5 = 6,000.
        var pay = OvertimePayCalculator.Calculate(225_000m, overtimeHours25: 10m, overtimeHours50: 4m);

        pay.ShouldBe(18_500m);
    }

    [Fact]
    public void Calculate_ZeroHours_ReturnsZero()
    {
        OvertimePayCalculator.Calculate(225_000m, 0m, 0m).ShouldBe(0m);
    }

    [Fact]
    public void Calculate_NegativeHours_Throws()
    {
        Should.Throw<ArgumentException>(() => OvertimePayCalculator.Calculate(225_000m, -1m, 0m));
        Should.Throw<ArgumentException>(() => OvertimePayCalculator.Calculate(225_000m, 0m, -1m));
    }

    [Fact]
    public void HourlyRate_NonPositiveSalary_Throws()
    {
        Should.Throw<ArgumentException>(() => OvertimePayCalculator.HourlyRate(0m));
    }
}
