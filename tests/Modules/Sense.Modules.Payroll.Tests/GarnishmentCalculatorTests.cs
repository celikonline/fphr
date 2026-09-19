using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Pure unit tests for <see cref="GarnishmentCalculator"/> (senseik-019 2c, İİK m.83 basitleştirmesi).</summary>
public sealed class GarnishmentCalculatorTests
{
    [Fact]
    public void NoOrders_ReturnsZeroDeduction()
    {
        var result = GarnishmentCalculator.Calculate(100_000m, []);

        result.TotalDeduction.ShouldBe(0m);
        result.AppliedAmounts.ShouldBeEmpty();
    }

    [Fact]
    public void ZeroOrNegativeNet_ReturnsZeroDeductionEvenWithOrders()
    {
        var orders = new[] { new GarnishmentDeductionInput(Guid.NewGuid(), GarnishmentType.Nafaka, 1_000m, 5_000m) };

        GarnishmentCalculator.Calculate(0m, orders).TotalDeduction.ShouldBe(0m);
        GarnishmentCalculator.Calculate(-500m, orders).TotalDeduction.ShouldBe(0m);
    }

    [Fact]
    public void NafakaOnly_DeductsFullInstallmentUncappedByIcraRate()
    {
        // Nafaka net ücretin çok üzerinde bir oranda (%50) kesilse bile İİK m.83 kesinti oranı sınırı yoktur.
        var orderId = Guid.NewGuid();
        var orders = new[] { new GarnishmentDeductionInput(orderId, GarnishmentType.Nafaka, 60_000m, 100_000m) };

        var result = GarnishmentCalculator.Calculate(100_000m, orders);

        result.TotalDeduction.ShouldBe(60_000m);
        result.AppliedAmounts.ShouldHaveSingleItem();
        result.AppliedAmounts[0].ShouldBe(new GarnishmentAppliedAmount(orderId, 60_000m));
    }

    [Fact]
    public void NafakaCappedByRemainingBalance_NotByInstallment()
    {
        var orderId = Guid.NewGuid();
        var orders = new[] { new GarnishmentDeductionInput(orderId, GarnishmentType.Nafaka, 5_000m, 1_200m) };

        var result = GarnishmentCalculator.Calculate(100_000m, orders);

        result.TotalDeduction.ShouldBe(1_200m); // son taksit: kalan bakiye taksit tutarından az
    }

    [Fact]
    public void IcraOnly_CappedAtQuarterOfNetBeforeDeductions()
    {
        var orderId = Guid.NewGuid();
        var orders = new[] { new GarnishmentDeductionInput(orderId, GarnishmentType.Icra, 50_000m, 500_000m) };

        var result = GarnishmentCalculator.Calculate(100_000m, orders);

        result.TotalDeduction.ShouldBe(25_000m); // %25 tavanı, taksit (50,000) tavanı aşıyor
    }

    [Fact]
    public void MultipleIcraOrders_ShareTheQuarterPoolInListOrder()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var orders = new[]
        {
            new GarnishmentDeductionInput(firstId, GarnishmentType.Icra, 20_000m, 200_000m),
            new GarnishmentDeductionInput(secondId, GarnishmentType.Icra, 20_000m, 200_000m),
        };

        // Havuz = 100,000 * %25 = 25,000. İlk emir 20,000 alır, ikinci emre yalnız kalan 5,000 kalır.
        var result = GarnishmentCalculator.Calculate(100_000m, orders);

        result.TotalDeduction.ShouldBe(25_000m);
        result.AppliedAmounts.ShouldContain(new GarnishmentAppliedAmount(firstId, 20_000m));
        result.AppliedAmounts.ShouldContain(new GarnishmentAppliedAmount(secondId, 5_000m));
    }

    [Fact]
    public void NafakaThenIcra_IcraPoolBasedOnNetBeforeDeductions_NotOnRemainderAfterNafaka()
    {
        var nafakaId = Guid.NewGuid();
        var icraId = Guid.NewGuid();
        var orders = new[]
        {
            new GarnishmentDeductionInput(nafakaId, GarnishmentType.Nafaka, 10_000m, 50_000m),
            new GarnishmentDeductionInput(icraId, GarnishmentType.Icra, 100_000m, 500_000m),
        };

        // Nafaka 10,000 keser. İcra havuzu netBeforeDeductions (100,000) üzerinden %25 = 25,000 (nafakadan sonraki
        // kalan net üzerinden değil - GarnishmentCalculator'ın belgelenmiş varsayımı).
        var result = GarnishmentCalculator.Calculate(100_000m, orders);

        result.TotalDeduction.ShouldBe(35_000m);
        result.AppliedAmounts.ShouldContain(new GarnishmentAppliedAmount(nafakaId, 10_000m));
        result.AppliedAmounts.ShouldContain(new GarnishmentAppliedAmount(icraId, 25_000m));
    }
}
