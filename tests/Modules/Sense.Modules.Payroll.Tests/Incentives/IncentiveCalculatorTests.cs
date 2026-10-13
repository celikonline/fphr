using System.Text.Json;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Incentives;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>
/// PAY-INCENTIVE-3: the pure engine with incentive effects, hand-computed against the REAL 2026 legislation values
/// (<see cref="IncentiveTestKit.Snapshot"/>): asgari ücret 33.030, SGK taban 33.030 / tavan 247.725, işçi SGK %14, işçi işsizlik %1,
/// işveren SGK %20,5, işveren işsizlik %2, damga binde 7,59, dilimler 190.000 / 400.000 (%15 / %20 ...). The plan's 33.030 example
/// (matrah 50.000, tavan 33.030, oran 0,155 -> 5.119,65) is exactly the real minimum wage.
/// </summary>
public sealed class IncentiveCalculatorTests
{
    private static readonly PayrollLegislationSnapshot Snapshot = IncentiveTestKit.Snapshot;
    private const string DiscountKey = "Payroll.Sgk.EmployerDiscountRate";

    private static PayrollCalculationInput Plain(decimal salary) => IncentiveTestKit.Plain(salary);

    private static IncentiveEffectInput Discount(decimal rate, string key = DiscountKey, string code = "SGK_5510_MD81") =>
        new(code, IncentiveKind.EmployerSgkRateDiscount, new ResolvedRate(rate, key));

    private static IncentiveEffectInput Relief(decimal rate, string code = "ARGE_5746_GV") =>
        new(code, IncentiveKind.IncomeTaxWithholdingRelief, new ResolvedRate(rate, $"Incentive.{code}.Rate"));

    private static IncentiveEffectInput Coverage(
        IncentiveCoverageBase coverageBase = IncentiveCoverageBase.MinimumWage, decimal share = 1m, decimal? cap = 1m, decimal? employerSgkRate = 0.155m,
        bool employerSgk = true, bool employerUnemp = false, bool employeeSgk = false, bool employeeUnemp = false, string code = "SGK_17103") =>
        new(code, IncentiveKind.EmployerShareCoverage, new ResolvedRate(share, $"Incentive.{code}.Rate"), coverageBase, cap,
            employerSgkRate is null ? null : new ResolvedRate(employerSgkRate.Value, $"Incentive.{code}.EmployerSgkRate"),
            employerSgk, employerUnemp, employeeSgk, employeeUnemp);

    private static PayrollCalculationInput With(PayrollCalculationInput input, params IncentiveEffectInput[] effects) => input with { Incentives = effects };

    // ---- (a) EMPLOYER_DISCOUNT ----

    [Fact]
    public void RateDiscount_ByLegislationKey_GivesTheSameAmountsAsTheLegacyFivePoints_AndNamesTheKeyAndTheIncentive()
    {
        // 200.000 brüt: SGK matrahı 200.000 (tavanın altı); 200.000 x 0,05 = 10.000. İşveren maliyeti 200.000 + 41.000 - 10.000 + 4.000 = 235.000.
        var legacy = PayrollCalculator.Calculate(Plain(200_000m) with { IncentiveEligible = true }, Snapshot);

        var result = PayrollCalculator.Calculate(With(Plain(200_000m), Discount(0.05m)), Snapshot);

        var step = result.Step(PayrollTraceStepCodes.EmployerDiscount);
        step.Output.ShouldBe(10_000m);
        result.EmployerCost.ShouldBe(235_000m);
        result.EmployerCost.ShouldBe(legacy.EmployerCost);
        result.NetAmount.ShouldBe(legacy.NetAmount);
        step.Inputs["incentiveCode"].ShouldBe("SGK_5510_MD81");
        step.Inputs["incentiveEligible"].ShouldBe("1");
        step.Inputs["discountRate"].ShouldBe("0.05");
        step.ParameterRefs.ShouldHaveSingleItem().Key.ShouldBe(DiscountKey);
        step.ParameterRefs[0].Value.ShouldBe(0.05m);
        step.Formula.ShouldContain("SGK_5510_MD81");
        result.Steps.Select(s => s.Code).ShouldNotContain(PayrollTraceStepCodes.EmployerIncentive);
    }

    [Fact]
    public void RateDiscount_ByLiteralRate_GivesTheSameResult_ReferencingTheIncentiveRateKey()
    {
        var byKey = PayrollCalculator.Calculate(With(Plain(200_000m), Discount(0.05m)), Snapshot);

        var literal = PayrollCalculator.Calculate(With(Plain(200_000m), Discount(0.05m, "Incentive.SGK_5510_MD81.Rate")), Snapshot);

        literal.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(10_000m);
        literal.EmployerCost.ShouldBe(byKey.EmployerCost);
        literal.NetAmount.ShouldBe(byKey.NetAmount);
        literal.Step(PayrollTraceStepCodes.EmployerDiscount).ParameterRefs.ShouldHaveSingleItem().Key.ShouldBe("Incentive.SGK_5510_MD81.Rate");
    }

    [Fact]
    public void RateDiscount_ReplacesTheLegacyRule_AnotherRateGivesAnotherAmount()
    {
        // 200.000 x 0,10 = 20.000 (not 10.000): a rate discount replaces the flat 5 points even if the legacy flag were still set.
        var result = PayrollCalculator.Calculate(With(Plain(200_000m) with { IncentiveEligible = true }, Discount(0.10m, "Incentive.X.Rate", "X")), Snapshot);

        result.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(20_000m);
        result.EmployerCost.ShouldBe(225_000m); // 200.000 + 41.000 - 20.000 + 4.000
    }

    [Fact]
    public void RateDiscount_AppliesOnTheCappedSgkBase()
    {
        // 300.000 brüt: SGK matrahı tavanda 247.725; 247.725 x 0,05 = 12.386,25.
        var result = PayrollCalculator.Calculate(With(Plain(300_000m), Discount(0.05m)), Snapshot);

        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(247_725m);
        result.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(12_386.25m);
    }

    // ---- (b) EMPLOYER_INCENTIVE ----

    [Fact]
    public void Coverage_MinimumWageBase_CoversTheEmployerSgkOfTheCappedBase()
    {
        // Brüt 50.000 = SGK matrahı. Taban = min(50.000, 33.030 x 1 x 1) = 33.030. İşveren SGK payı 50.000 x 0,205 = 10.250;
        // karşılanan = min(10.250, 33.030 x 0,155 x 1 = 5.119,65) = 5.119,65. Maliyet = 50.000 + 10.250 + 1.000 - 5.119,65 = 56.130,35.
        var plain = PayrollCalculator.Calculate(Plain(50_000m), Snapshot);

        var result = PayrollCalculator.Calculate(With(Plain(50_000m), Coverage()), Snapshot);

        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Output.ShouldBe(5_119.65m);
        step.Inputs["premiumBase"].ShouldBe("33030");
        step.Inputs["minimumWageCap"].ShouldBe("33030");
        step.Inputs["employerSgkCovered"].ShouldBe("5119.65");
        step.ParameterRefs.Select(r => r.Key).ShouldContain("Payroll.MinimumWage.Gross");
        step.ParameterRefs.Single(r => r.Key == "Payroll.MinimumWage.Gross").Value.ShouldBe(33_030m);
        result.EmployerCost.ShouldBe(56_130.35m);
        result.EmployerCost.ShouldBe(plain.EmployerCost - 5_119.65m);
        result.NetAmount.ShouldBe(plain.NetAmount);
        result.Steps.Select(s => s.Code).SkipWhile(c => c != PayrollTraceStepCodes.EmployerUnemp).ShouldBe(
            [PayrollTraceStepCodes.EmployerUnemp, PayrollTraceStepCodes.EmployerIncentive, PayrollTraceStepCodes.EmployerCost]);
        var cost = result.Step(PayrollTraceStepCodes.EmployerCost);
        cost.Formula.ShouldBe("gross + EMPLOYER_SGK - EMPLOYER_DISCOUNT + EMPLOYER_UNEMP - EMPLOYER_INCENTIVE");
        cost.Inputs["employerIncentive"].ShouldBe("5119.65");
        cost.Inputs.ContainsKey("incomeTaxRelief").ShouldBeFalse();
    }

    [Fact]
    public void Coverage_InAPartialMonth_ScalesTheMinimumWageCapByThePaidDays()
    {
        // 15 gün kıst, aylık 50.000 -> brüt 25.000, oran 15/30. SGK tabanı 16.515 (= 33.030 x 0,5), matrah 25.000.
        // Taban = min(25.000, 33.030 x 1 x 0,5 = 16.515) = 16.515. İşveren SGK 25.000 x 0,205 = 5.125; karşılanan = min(5.125, 16.515 x 0,155 = 2.559,825 -> 2.559,83).
        var input = new PayrollCalculationInput(50_000m, 15, 30, MealBenefitVariant.None, 0m, 0m, IncentiveTestKit.AsOf, ProratedPaidDays: 15);

        var result = PayrollCalculator.Calculate(With(input, Coverage()), Snapshot);

        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(25_000m);
        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Inputs["minimumWageCap"].ShouldBe("16515");
        step.Inputs["proration"].ShouldBe("0.5");
        step.Inputs["premiumBase"].ShouldBe("16515");
        step.Output.ShouldBe(2_559.83m);
    }

    [Fact]
    public void Coverage_WhenTheSgkBaseIsBelowTheCap_UsesTheSgkBase()
    {
        // Çarpan 2: tavan 66.060; matrah 50.000 < tavan -> taban = 50.000. Karşılanan = min(10.250, 50.000 x 0,155 = 7.750) = 7.750.
        var result = PayrollCalculator.Calculate(With(Plain(50_000m), Coverage(cap: 2m)), Snapshot);

        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Inputs["minimumWageCap"].ShouldBe("66060");
        step.Inputs["premiumBase"].ShouldBe("50000");
        step.Output.ShouldBe(7_750m);
    }

    [Fact]
    public void Coverage_ActualSgkBase_IgnoresTheCap_AndTheShareScalesTheCoveredAmount()
    {
        // Gerçek matrah 50.000, pay 0,5: Round(50.000 x 0,155 x 0,5) = 3.875 (< işveren SGK 10.250). Asgari ücret izde yok.
        var result = PayrollCalculator.Calculate(With(Plain(50_000m), Coverage(IncentiveCoverageBase.ActualSgkBase, share: 0.5m, cap: null)), Snapshot);

        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Output.ShouldBe(3_875m);
        step.Inputs["premiumBase"].ShouldBe("50000");
        step.Inputs.ContainsKey("minimumWageCap").ShouldBeFalse();
        step.ParameterRefs.Select(r => r.Key).ShouldNotContain("Payroll.MinimumWage.Gross");
    }

    [Fact]
    public void Coverage_OfTheEmployerUnemploymentShare()
    {
        // Taban 33.030; Round(33.030 x 0,02 x 1) = 660,60 (< işveren işsizlik 50.000 x 0,02 = 1.000). Maliyet = 50.000 + 10.250 + 1.000 - 660,60 = 60.589,40.
        var result = PayrollCalculator.Calculate(With(Plain(50_000m), Coverage(employerSgk: false, employerUnemp: true, employerSgkRate: null)), Snapshot);

        result.Step(PayrollTraceStepCodes.EmployerIncentive).Output.ShouldBe(660.60m);
        result.EmployerCost.ShouldBe(60_589.40m);
        result.Step(PayrollTraceStepCodes.EmployerIncentive).ParameterRefs.Select(r => r.Key).ShouldContain("Payroll.Unemployment.EmployerRate");
    }

    [Fact]
    public void Coverage_OfTheEmployeeShares_LowersTheEmployerCostButNeverTheNet()
    {
        // İşçi SGK 50.000 x 0,14 = 7.000, işçi işsizlik 500. Taban 33.030: min(7.000, 4.624,20) + min(500, 330,30) = 4.954,50.
        var plain = PayrollCalculator.Calculate(Plain(50_000m), Snapshot);

        var result = PayrollCalculator.Calculate(With(Plain(50_000m), Coverage(employerSgk: false, employeeSgk: true, employeeUnemp: true, employerSgkRate: null)), Snapshot);

        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Inputs["employeeSgkCovered"].ShouldBe("4624.2");
        step.Inputs["employeeUnempCovered"].ShouldBe("330.3");
        step.Output.ShouldBe(4_954.50m);
        result.EmployerCost.ShouldBe(plain.EmployerCost - 4_954.50m);
        result.NetAmount.ShouldBe(plain.NetAmount);
        result.Step(PayrollTraceStepCodes.SgkEmployee).Output.ShouldBe(plain.Step(PayrollTraceStepCodes.SgkEmployee).Output);
        result.Step(PayrollTraceStepCodes.Net).Output.ShouldBe(plain.Step(PayrollTraceStepCodes.Net).Output);
    }

    [Fact]
    public void Coverage_NeverExceedsThePremiumItCovers()
    {
        // Brüt 33.030 (matrah 33.030), çarpan 10, hayali işveren oranı %30: Round(33.030 x 0,30) = 9.909 > işveren SGK payı Round(33.030 x 0,205) = 6.771,15.
        // Karşılanan prim tutarını aşamaz -> 6.771,15. Pay 1, her dört prim açık: işveren işsizlik 660,60; işçi SGK 4.624,20; işçi işsizlik 330,30.
        var effect = Coverage(cap: 10m, employerSgkRate: 0.30m, employerSgk: true, employerUnemp: true, employeeSgk: true, employeeUnemp: true);

        var result = PayrollCalculator.Calculate(With(Plain(33_030m), effect), Snapshot);

        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Inputs["employerSgkCovered"].ShouldBe("6771.15");
        step.Inputs["employerUnempCovered"].ShouldBe("660.6");
        step.Inputs["employeeSgkCovered"].ShouldBe("4624.2");
        step.Inputs["employeeUnempCovered"].ShouldBe("330.3");
        step.Output.ShouldBe(12_386.25m); // 6.771,15 + 660,60 + 4.624,20 + 330,30
    }

    [Fact]
    public void Coverage_CapsTheEmployerSgkCoverageAtThePremiumLeftAfterTheEmployerDiscount()
    {
        // Eski 5 puan hâlâ açıksa (yalnız hesap güvencesi; servis a/b varken bunu kapatır): işveren SGK 10.250 - indirim 2.500 = 7.750 kalır;
        // gerçek matrah 50.000 x %30 = 15.000 isteği 7.750'ye iner.
        var effect = Coverage(IncentiveCoverageBase.ActualSgkBase, cap: null, employerSgkRate: 0.30m);

        var result = PayrollCalculator.Calculate(With(Plain(50_000m) with { IncentiveEligible = true }, effect), Snapshot);

        result.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(2_500m);
        result.Step(PayrollTraceStepCodes.EmployerIncentive).Output.ShouldBe(7_750m);
    }

    [Fact]
    public void Coverage_ForADailyWagedEmployee_ScalesTheCapByThePaidDays()
    {
        // Günlük 2.000 x 22 ücretli gün = 44.000 brüt, oran 22/30: tavan Round(33.030 x 1 x 22/30) = 24.222. Matrah 44.000 -> taban 24.222.
        // Karşılanan = min(44.000 x 0,205 = 9.020, 24.222 x 0,155 = 3.754,41) = 3.754,41.
        var input = new PayrollCalculationInput(60_000m, 22, 30, MealBenefitVariant.None, 0m, 0m, IncentiveTestKit.AsOf,
            ProratedPaidDays: 22, Wage: new PeriodWage(WageBasis.Daily, 2_000m, 22));

        var result = PayrollCalculator.Calculate(With(input, Coverage()), Snapshot);

        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(44_000m);
        var step = result.Step(PayrollTraceStepCodes.EmployerIncentive);
        step.Inputs["minimumWageCap"].ShouldBe("24222");
        step.Output.ShouldBe(3_754.41m);
    }

    [Fact]
    public void Coverage_RaisesWhenAnIncompleteEffectReachesTheEngine()
    {
        Should.Throw<Exception>(() => PayrollCalculator.Calculate(With(Plain(50_000m), Coverage(employerSgkRate: null)), Snapshot));
        Should.Throw<Exception>(() => PayrollCalculator.Calculate(With(Plain(50_000m), Coverage(cap: null)), Snapshot));
    }

    // ---- (c) INCOME_TAX_RELIEF ----

    [Fact]
    public void Relief_IsTheShareOfTheIncomeTaxAfterTheMinimumWageExemption_AndTheNetIsUnchanged()
    {
        // 200.000 brüt: GV matrahı 170.000, vergi 170.000 x %15 = 25.500; asgari ücret istisnası Round(Round(33.030 x 0,85) x 0,15) = Round(28.075,50 x 0,15) = 4.211,33;
        // istisna sonrası 21.288,67; x 0,8 = 17.030,936 -> 17.030,94. Maliyet = 200.000 + 41.000 + 4.000 - 17.030,94 = 227.969,06.
        var plain = PayrollCalculator.Calculate(Plain(200_000m), Snapshot);

        var result = PayrollCalculator.Calculate(With(Plain(200_000m), Relief(0.8m)), Snapshot);

        result.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(25_500m);
        result.Step(PayrollTraceStepCodes.MinWageExemptionGv).Output.ShouldBe(4_211.33m);
        var step = result.Step(PayrollTraceStepCodes.IncomeTaxRelief);
        step.Inputs["incomeTaxAfterExemption"].ShouldBe("21288.67");
        step.Output.ShouldBe(17_030.94m);
        result.EmployerCost.ShouldBe(227_969.06m);
        result.NetAmount.ShouldBe(plain.NetAmount);
        result.Step(PayrollTraceStepCodes.Net).Output.ShouldBe(plain.Step(PayrollTraceStepCodes.Net).Output);
        step.ParameterRefs.ShouldHaveSingleItem().Key.ShouldBe("Incentive.ARGE_5746_GV.Rate");
        var cost = result.Step(PayrollTraceStepCodes.EmployerCost);
        cost.Formula.ShouldBe("gross + EMPLOYER_SGK - EMPLOYER_DISCOUNT + EMPLOYER_UNEMP - INCOME_TAX_RELIEF");
        cost.Inputs["incomeTaxRelief"].ShouldBe("17030.94");
        result.Steps.Select(s => s.Code).SkipWhile(c => c != PayrollTraceStepCodes.EmployerUnemp).ShouldBe(
            [PayrollTraceStepCodes.EmployerUnemp, PayrollTraceStepCodes.IncomeTaxRelief, PayrollTraceStepCodes.EmployerCost]);
    }

    [Fact]
    public void Relief_IsZeroWhenThereIsNoIncomeTaxToRelieve()
    {
        // Brüt 33.030 (asgari ücret): vergi 4.211,33, asgari ücret istisnası 4.211,33 -> istisna sonrası 0 -> terkin 0.
        var result = PayrollCalculator.Calculate(With(Plain(33_030m), Relief(0.8m)), Snapshot);

        result.Step(PayrollTraceStepCodes.IncomeTaxRelief).Output.ShouldBe(0m);
        result.EmployerCost.ShouldBe(PayrollCalculator.Calculate(Plain(33_030m), Snapshot).EmployerCost);
    }

    [Fact]
    public void Relief_TogetherWithTheLegacyFivePoints_AppliesBothEffects()
    {
        // İki etki: 5 puan indirim 10.000 + terkin 17.030,94 -> maliyet 245.000 - 10.000 - 17.030,94 = 217.969,06 (vergi teşviki profil kodunu etkilemez).
        var result = PayrollCalculator.Calculate(With(Plain(200_000m) with { IncentiveEligible = true }, Relief(0.8m)), Snapshot);

        result.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(10_000m);
        result.Step(PayrollTraceStepCodes.EmployerDiscount).Inputs.ContainsKey("incentiveCode").ShouldBeFalse(); // legacy step, unchanged
        result.Step(PayrollTraceStepCodes.IncomeTaxRelief).Output.ShouldBe(17_030.94m);
        result.EmployerCost.ShouldBe(217_969.06m);
    }

    [Fact]
    public void Relief_TogetherWithARateDiscount_AppliesBothAndKeepsTheOrder()
    {
        var result = PayrollCalculator.Calculate(With(Plain(200_000m), Discount(0.05m), Relief(0.8m)), Snapshot);

        result.EmployerCost.ShouldBe(217_969.06m);
        result.Steps.Select(s => s.Code).TakeLast(5).ShouldBe(
            [PayrollTraceStepCodes.EmployerSgk, PayrollTraceStepCodes.EmployerDiscount, PayrollTraceStepCodes.EmployerUnemp, PayrollTraceStepCodes.IncomeTaxRelief, PayrollTraceStepCodes.EmployerCost]);
    }

    [Fact]
    public void CoverageAndRelief_Together_SubtractBothFromTheEmployerCost()
    {
        var result = PayrollCalculator.Calculate(With(Plain(200_000m), Coverage(IncentiveCoverageBase.ActualSgkBase, cap: null), Relief(0.8m)), Snapshot);

        var cost = result.Step(PayrollTraceStepCodes.EmployerCost);
        cost.Formula.ShouldBe("gross + EMPLOYER_SGK - EMPLOYER_DISCOUNT + EMPLOYER_UNEMP - EMPLOYER_INCENTIVE - INCOME_TAX_RELIEF");
        // Karşılanan: min(41.000, 200.000 x 0,155 = 31.000) = 31.000. Maliyet = 200.000 + 41.000 + 4.000 - 31.000 - 17.030,94 = 196.969,06.
        result.Step(PayrollTraceStepCodes.EmployerIncentive).Output.ShouldBe(31_000m);
        result.EmployerCost.ShouldBe(196_969.06m);
    }

    // ---- (d) and the "no incentive" guarantees ----

    [Fact]
    public void DeclarationOnly_AndEmptyOrNullLists_LeaveTheTraceExactlyAsWithoutIncentives()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plain = JsonSerializer.Serialize(PayrollCalculator.Calculate(Plain(200_000m), Snapshot).Steps, options);

        JsonSerializer.Serialize(PayrollCalculator.Calculate(Plain(200_000m) with { Incentives = [] }, Snapshot).Steps, options).ShouldBe(plain);
        var declarationOnly = new IncentiveEffectInput("BEYAN", IncentiveKind.DeclarationOnly, null);
        JsonSerializer.Serialize(PayrollCalculator.Calculate(With(Plain(200_000m), declarationOnly), Snapshot).Steps, options).ShouldBe(plain);
    }

    [Fact]
    public void NoIncentiveSteps_AreAddedWithoutTheirIncentive()
    {
        var codes = PayrollCalculator.Calculate(With(Plain(200_000m), Discount(0.05m)), Snapshot).Steps.Select(s => s.Code).ToList();

        codes.ShouldNotContain(PayrollTraceStepCodes.EmployerIncentive);
        codes.ShouldNotContain(PayrollTraceStepCodes.IncomeTaxRelief);
        codes.Count.ShouldBe(18); // the same 18 steps as an employee without incentives
    }

    [Fact]
    public void NoIncentiveKind_ChangesTheEmployeesNetOrGross()
    {
        var plain = PayrollCalculator.Calculate(Plain(200_000m), Snapshot);
        var combinations = new[]
        {
            new[] { Discount(0.05m) },
            new[] { Coverage(employerUnemp: true, employeeSgk: true, employeeUnemp: true) },
            new[] { Relief(0.8m) },
            new[] { Discount(0.05m), Relief(0.8m) },
        };

        foreach (var effects in combinations)
        {
            var result = PayrollCalculator.Calculate(With(Plain(200_000m), effects), Snapshot);

            result.NetAmount.ShouldBe(plain.NetAmount);
            result.GrossAmount.ShouldBe(plain.GrossAmount);
            result.EmployerCost.ShouldBeLessThan(plain.EmployerCost);
        }
    }

    [Fact]
    public void TwoSgkCategoryEffects_AreRejected()
    {
        Should.Throw<Exception>(() => PayrollCalculator.Calculate(With(Plain(200_000m), Discount(0.05m), Coverage()), Snapshot));
    }
}
