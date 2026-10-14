using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>
/// PAY-DEDUCTION-1: the pure engine with formula deductions. Reference month: brüt 100,000, aylık ücretli, first month, no meal.
/// Hand calculation (snapshot below): SGK_BASE 100,000 (taban 30,000 / tavan 225,000) -> SGK işçi 14,000, işsizlik 1,000;
/// GV_BASE 85,000 -> gelir vergisi 12,750 (%15), asgari ücret istisnası 30,000 x (1 - 0.15) x 0.15 = 3,825 -> 8,925;
/// damga 100,000 x 0.00759 = 759.00, istisna 30,000 x 0.00759 = 227.70 -> 531.30;
/// net (kesinti öncesi) = 100,000 - 14,000 - 1,000 - 8,925 - 531.30 = 75,543.70.
/// </summary>
public sealed class PayDeductionCalculationTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 1);
    private const decimal NetBeforeDeductions = 75_543.70m;

    private static readonly PayrollLegislationSnapshot Snapshot = new(
        [new TaxBracket(158_000m, 0.15m), new TaxBracket(330_000m, 0.20m), new TaxBracket(1_200_000m, 0.27m), new TaxBracket(4_300_000m, 0.35m), new TaxBracket(null, 0.40m)],
        30_000m, 225_000m, 0.14m, 0.01m, 0.205m, 0.02m, 0.05m, 0.00759m, 30_000m, 300m, 158m, false, 12000m, 7000m, 3000m, AsOf);

    private static PayrollCalculationInput Input(
        IReadOnlyList<PayItemDeduction>? deductions = null, decimal advance = 0m, decimal union = 0m,
        IReadOnlyList<GarnishmentDeductionInput>? garnishments = null, IReadOnlyList<PayItemEarning>? earnings = null) =>
        new(100_000m, 30, 30, MealBenefitVariant.None, 0m, 0m, AsOf,
            AdvanceDeductionTotal: advance, GarnishmentOrders: garnishments, UnionDuesAmount: union, PayItems: earnings, PayItemDeductions: deductions);

    private static PayItemDeduction Deduction(string code, decimal amount) =>
        new(code, code + " adı", amount, "brut * 0.01", new Dictionary<string, decimal> { ["brut"] = 100_000m });

    private static TraceStep PayItemStep(PayrollCalculationResult result, string code) =>
        result.Steps.Single(s => s.Code == PayrollTraceStepCodes.PayItem && s.PayItem == code);

    [Fact]
    public void WithoutDeductions_TheTraceIsTheOldOne()
    {
        var result = PayrollCalculator.Calculate(Input(), Snapshot);

        result.NetAmount.ShouldBe(NetBeforeDeductions);
        result.Steps.ShouldNotContain(s => s.Code == PayrollTraceStepCodes.PayItem);
        var deductions = result.Step(PayrollTraceStepCodes.Deductions);
        deductions.Inputs.ShouldNotContainKey("payItemDeductionsTotal");
        deductions.Formula.ShouldNotContain("formüllü");
        result.Step(PayrollTraceStepCodes.Net).Formula.ShouldNotContain("formüllü");
        (result.CappedPayItemDeductionCodes ?? []).ShouldBeEmpty();
    }

    [Fact]
    public void EmptyDeductionList_IsTheSameAsNone()
    {
        var withNone = PayrollCalculator.Calculate(Input(), Snapshot);
        var withEmpty = PayrollCalculator.Calculate(Input([]), Snapshot);

        withEmpty.Steps.Count.ShouldBe(withNone.Steps.Count);
        withEmpty.Step(PayrollTraceStepCodes.Deductions).Inputs.ShouldBe(withNone.Step(PayrollTraceStepCodes.Deductions).Inputs);
    }

    [Fact]
    public void Deduction_LowersTheNetByItsAmount_AndTouchesNeitherGrossNorAnyTaxBase()
    {
        var baseline = PayrollCalculator.Calculate(Input(), Snapshot);

        // brut * 0.01 = 1,000.00
        var result = PayrollCalculator.Calculate(Input([Deduction("SENDIKA_EK", 1_000m)]), Snapshot);

        result.GrossAmount.ShouldBe(100_000m);
        result.NetAmount.ShouldBe(NetBeforeDeductions - 1_000m); // 74,543.70
        result.EmployerCost.ShouldBe(baseline.EmployerCost);
        foreach (var code in new[]
        {
            PayrollTraceStepCodes.Earnings, PayrollTraceStepCodes.SgkBase, PayrollTraceStepCodes.SgkEmployee, PayrollTraceStepCodes.UnempEmployee,
            PayrollTraceStepCodes.GvBase, PayrollTraceStepCodes.CumulativeBase, PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.StampTax,
            PayrollTraceStepCodes.EmployerSgk, PayrollTraceStepCodes.EmployerCost,
        })
        {
            result.Step(code).Output.ShouldBe(baseline.Step(code).Output, code);
            result.Step(code).Inputs.ShouldBe(baseline.Step(code).Inputs, code);
        }

        result.CappedPayItemDeductionCodes.ShouldBeEmpty();
    }

    [Fact]
    public void Deduction_Trace_PayItemStepBeforeDeductions_AndDeductionsTotal()
    {
        var result = PayrollCalculator.Calculate(Input([Deduction("SENDIKA_EK", 1_000m)]), Snapshot);

        var step = PayItemStep(result, "SENDIKA_EK");
        step.Inputs["direction"].ShouldBe("deduction");
        step.Inputs["deductionAmount"].ShouldBe("1000");
        step.Inputs["variable.brut"].ShouldBe("100000");
        step.Inputs.ShouldNotContainKey("capped");
        step.Output.ShouldBe(1_000m);
        step.Formula.ShouldBe("brut * 0.01");
        step.Order.ShouldBeLessThan(result.Step(PayrollTraceStepCodes.Deductions).Order);
        var deductions = result.Step(PayrollTraceStepCodes.Deductions);
        deductions.Inputs["payItemDeductionsTotal"].ShouldBe("1000");
        deductions.Output.ShouldBe(1_000m);
        deductions.Formula.ShouldContain("formüllü kesintiler");
        result.Step(PayrollTraceStepCodes.Net).Formula.ShouldContain("formüllü kesintiler");
        result.Steps.Select(s => s.Order).ShouldBe(Enumerable.Range(1, result.Steps.Count));
    }

    [Fact]
    public void Deduction_ComesAfterIcraAndAvans_AndBeforeSendika()
    {
        // icra (nafaka) 5,000 + avans 2,000 -> 7,000; formüllü 800; sendika 200. Net = 75,543.70 - 8,000 = 67,543.70.
        var garnishments = new[] { new GarnishmentDeductionInput(Guid.NewGuid(), GarnishmentType.Nafaka, 5_000m, 50_000m) };

        var result = PayrollCalculator.Calculate(Input([Deduction("A_KESINTI", 800m)], advance: 2_000m, union: 200m, garnishments: garnishments), Snapshot);

        var deductions = result.Step(PayrollTraceStepCodes.Deductions);
        deductions.Inputs["garnishmentDeductionTotal"].ShouldBe("5000");
        deductions.Inputs["advanceDeductionTotal"].ShouldBe("2000");
        deductions.Inputs["payItemDeductionsTotal"].ShouldBe("800");
        deductions.Inputs["unionDues"].ShouldBe("200");
        deductions.Output.ShouldBe(8_000m);
        result.NetAmount.ShouldBe(67_543.70m);
    }

    [Fact]
    public void Deductions_AreCappedAtTheRemainingNetAfterIcraAndAvans_FromTheLastCodeBackwards()
    {
        // avans 74,843.70 leaves 700.00 of the 75,543.70 net. A 300 + B 500 + C 400 = 1,200 -> excess 500:
        // C is cut first (400 -> 0, 100 left to cut), then B (500 -> 400). A stays 300 -> 300 + 400 + 0 = 700, net 0.
        var result = PayrollCalculator.Calculate(
            Input([Deduction("C_KESINTI", 400m), Deduction("A_KESINTI", 300m), Deduction("B_KESINTI", 500m)], advance: 74_843.70m), Snapshot);

        PayItemStep(result, "A_KESINTI").Output.ShouldBe(300m);
        PayItemStep(result, "B_KESINTI").Output.ShouldBe(400m);
        PayItemStep(result, "C_KESINTI").Output.ShouldBe(0m);
        PayItemStep(result, "B_KESINTI").Inputs["capped"].ShouldBe("1");
        PayItemStep(result, "B_KESINTI").Inputs["deductionAmount"].ShouldBe("500");
        PayItemStep(result, "A_KESINTI").Inputs.ShouldNotContainKey("capped");
        result.CappedPayItemDeductionCodes.ShouldBe(["C_KESINTI", "B_KESINTI"]);
        result.Step(PayrollTraceStepCodes.Deductions).Inputs["payItemDeductionsTotal"].ShouldBe("700");
        result.NetAmount.ShouldBe(0m);
    }

    [Fact]
    public void Deductions_AreCappedAfterTheSendikaAidati_SendikaStaysWhole_NetIsNotNegative()
    {
        // avans 74,843.70 leaves 700.00; sendika 200 stays whole -> room for the formula deductions is 500.
        // A 300 + B 400 = 700 -> excess 200: B is cut (400 -> 200). Net = 75,543.70 - 74,843.70 - 500 - 200 = 0.
        var result = PayrollCalculator.Calculate(Input([Deduction("A_KESINTI", 300m), Deduction("B_KESINTI", 400m)], advance: 74_843.70m, union: 200m), Snapshot);

        PayItemStep(result, "A_KESINTI").Output.ShouldBe(300m);
        PayItemStep(result, "B_KESINTI").Output.ShouldBe(200m);
        result.Step(PayrollTraceStepCodes.Deductions).Inputs["unionDues"].ShouldBe("200");
        result.Step(PayrollTraceStepCodes.Deductions).Inputs["payItemDeductionsTotal"].ShouldBe("500");
        result.CappedPayItemDeductionCodes.ShouldBe(["B_KESINTI"]);
        result.NetAmount.ShouldBe(0m);
    }

    [Fact]
    public void Deductions_BiggerThanTheNet_GiveZeroNet_NeverNegative()
    {
        var result = PayrollCalculator.Calculate(Input([Deduction("DEV_KESINTI", 900_000m)]), Snapshot);

        PayItemStep(result, "DEV_KESINTI").Output.ShouldBe(NetBeforeDeductions);
        result.NetAmount.ShouldBe(0m);
        result.CappedPayItemDeductionCodes.ShouldBe(["DEV_KESINTI"]);
    }

    [Fact]
    public void Deductions_WhenAvansTookAlmostEverything_AreFullyCapped()
    {
        // avans is itself limited to the net: 80,000 -> 75,543.70, nothing is left for the deduction.
        var result = PayrollCalculator.Calculate(Input([Deduction("KESINTI", 100m)], advance: 80_000m), Snapshot);

        PayItemStep(result, "KESINTI").Output.ShouldBe(0m);
        result.NetAmount.ShouldBe(0m);
        result.CappedPayItemDeductionCodes.ShouldBe(["KESINTI"]);
    }

    [Fact]
    public void Deductions_ExactlyTheRemainingNet_AreNotCapped()
    {
        var result = PayrollCalculator.Calculate(Input([Deduction("KESINTI", NetBeforeDeductions)]), Snapshot);

        result.NetAmount.ShouldBe(0m);
        result.CappedPayItemDeductionCodes.ShouldBeEmpty();
        PayItemStep(result, "KESINTI").Inputs.ShouldNotContainKey("capped");
    }

    [Fact]
    public void CapOrder_IsByOrdinalCode_NotByTheOrderTheyAreGiven()
    {
        var one = PayrollCalculator.Calculate(Input([Deduction("B_KESINTI", 50_000m), Deduction("A_KESINTI", 50_000m)]), Snapshot);
        var two = PayrollCalculator.Calculate(Input([Deduction("A_KESINTI", 50_000m), Deduction("B_KESINTI", 50_000m)]), Snapshot);

        // 100,000 > 75,543.70: B (the last code) is cut by 24,456.30 -> 25,543.70; A stays 50,000.
        foreach (var result in new[] { one, two })
        {
            PayItemStep(result, "A_KESINTI").Output.ShouldBe(50_000m);
            PayItemStep(result, "B_KESINTI").Output.ShouldBe(25_543.70m);
            result.NetAmount.ShouldBe(0m);
            result.Steps.Where(s => s.Code == PayrollTraceStepCodes.PayItem).Select(s => s.PayItem).ShouldBe(["A_KESINTI", "B_KESINTI"]);
        }
    }

    [Fact]
    public void EarningAndDeductionTogether_GrossGrowsByTheEarningOnly_NetFallsByTheDeduction()
    {
        var earning = new PayItemEarning("YEMEK", "Yemek", 5_000m, true, true, true, "deger", new Dictionary<string, decimal> { ["deger"] = 5_000m });
        var withEarning = PayrollCalculator.Calculate(Input(earnings: [earning]), Snapshot);

        var both = PayrollCalculator.Calculate(Input([Deduction("KESINTI", 1_000m)], earnings: [earning]), Snapshot);

        both.GrossAmount.ShouldBe(105_000m);
        both.NetAmount.ShouldBe(withEarning.NetAmount - 1_000m);
        both.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(withEarning.Step(PayrollTraceStepCodes.StampTax).Output);
        both.Steps.Count(s => s.Code == PayrollTraceStepCodes.PayItem).ShouldBe(2);
        both.Step(PayrollTraceStepCodes.Earnings).Inputs["payItemsTotal"].ShouldBe("5000");
    }

    [Fact]
    public void NegativeDeductionAmount_IsRejected()
    {
        Should.Throw<Exception>(() => PayrollCalculator.Calculate(Input([Deduction("KESINTI", -1m)]), Snapshot));
    }
}
