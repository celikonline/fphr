using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// Hand-verifiable golden numbers for <see cref="ExitSettlementNetCalculator"/>. Round brackets (15% to 100k, 20% to 200k,
/// 27% to 500k, 35% above), SGK+işsizlik 15%, SGK ceiling 100k and damga vergisi 0.759% keep every figure checkable by hand.
/// </summary>
public sealed class ExitSettlementNetCalculatorTests
{
    private static readonly PayrollLegislationSnapshot Legislation = new(
        [new TaxBracket(100_000m, 0.15m), new TaxBracket(200_000m, 0.20m), new TaxBracket(500_000m, 0.27m), new TaxBracket(null, 0.35m)],
        SgkFloor: 0m, SgkCeiling: 100_000m, SgkEmployeeRate: 0.14m, UnemploymentEmployeeRate: 0.01m,
        SgkEmployerRate: 0m, UnemploymentEmployerRate: 0m, SgkEmployerDiscountRate: 0m, StampTaxRate: 0.00759m,
        MinimumWageGross: 0m, MealIncomeTaxDailyExemption: 0m, MealSgkDailyExemptionRate: 0m, MealMarketCardExemptionApplies: false,
        DisabilityMonthlyTaxReductionDegree1: 0m, DisabilityMonthlyTaxReductionDegree2: 0m, DisabilityMonthlyTaxReductionDegree3: 0m,
        AsOf: new DateOnly(2026, 8, 17));

    private static ExitPayInput Line(ExitPayKind kind, decimal gross) => new(Guid.NewGuid(), gross, ExitSettlementNetCalculator.DefaultTreatment(kind));

    [Fact]
    public void Notice_IsTaxedWithIncomeAndStampTax_ButNoSgk()
    {
        var result = ExitSettlementNetCalculator.Calculate([Line(ExitPayKind.Notice, 100_000m)], 0m, Legislation);

        result.SgkTotal.ShouldBe(0m);
        result.IncomeTaxTotal.ShouldBe(15_000m); // 100,000 * 15%
        result.StampTaxTotal.ShouldBe(759.00m); // 100,000 * 0.759%
        result.NetTotal.ShouldBe(84_241.00m);
    }

    [Fact]
    public void Severance_IsOnlySubjectToStampTax()
    {
        var result = ExitSettlementNetCalculator.Calculate([Line(ExitPayKind.Severance, 100_000m)], 0m, Legislation);

        result.SgkTotal.ShouldBe(0m);
        result.IncomeTaxTotal.ShouldBe(0m);
        result.StampTaxTotal.ShouldBe(759.00m);
        result.NetTotal.ShouldBe(99_241.00m);
    }

    [Fact]
    public void UnusedLeave_IsReducedBySgkBeforeIncomeTax()
    {
        var result = ExitSettlementNetCalculator.Calculate([Line(ExitPayKind.UnusedLeave, 10_000m)], 0m, Legislation);

        result.SgkTotal.ShouldBe(1_500.00m); // 10,000 * 15%
        result.IncomeTaxTotal.ShouldBe(1_275.00m); // (10,000 - 1,500) * 15%
        result.StampTaxTotal.ShouldBe(75.90m);
        result.NetTotal.ShouldBe(7_149.10m);
    }

    [Fact]
    public void IncomeTax_UsesThePriorCumulativeBase_SoTheHigherBracketApplies()
    {
        // Prior 90,000 + 20,000 = 110,000: 10,000 falls in the 20% bracket, 10,000 in the 15%: tax 3,500 rather than 3,000.
        var result = ExitSettlementNetCalculator.Calculate([Line(ExitPayKind.Notice, 20_000m)], 90_000m, Legislation);

        result.IncomeTaxTotal.ShouldBe(3_500.00m);
        result.StampTaxTotal.ShouldBe(151.80m);
        result.NetTotal.ShouldBe(16_348.20m);
    }

    [Fact]
    public void SgkBase_IsCappedAtTheMonthlyCeiling()
    {
        var result = ExitSettlementNetCalculator.Calculate([Line(ExitPayKind.UnusedLeave, 200_000m)], 0m, Legislation);

        result.SgkTotal.ShouldBe(15_000.00m); // capped base 100,000 * 15%
        result.IncomeTaxTotal.ShouldBe(32_000.00m); // base 185,000: 100,000*15% + 85,000*20%
        result.StampTaxTotal.ShouldBe(1_518.00m);
        result.NetTotal.ShouldBe(151_482.00m);
    }

    [Fact]
    public void TheIncomeTaxIsSharedAcrossLines_InProportionToTheirBase_AndAddsUpExactly()
    {
        var notice = Line(ExitPayKind.Notice, 60_000m);
        var rescission = Line(ExitPayKind.MutualRescissionPay, 40_000m);

        var result = ExitSettlementNetCalculator.Calculate([notice, rescission], 0m, Legislation);

        result.IncomeTaxTotal.ShouldBe(15_000m);
        result.Lines.Single(l => l.Key == notice.Key).IncomeTax.ShouldBe(9_000m);
        result.Lines.Single(l => l.Key == rescission.Key).IncomeTax.ShouldBe(6_000m);
        result.Lines.Sum(l => l.IncomeTax).ShouldBe(result.IncomeTaxTotal);
        result.GrossTotal.ShouldBe(100_000m);
    }

    [Fact]
    public void ANegativeGross_IsRejected()
    {
        Should.Throw<Exception>(() => ExitSettlementNetCalculator.Calculate([Line(ExitPayKind.Notice, -1m)], 0m, Legislation));
    }
}
