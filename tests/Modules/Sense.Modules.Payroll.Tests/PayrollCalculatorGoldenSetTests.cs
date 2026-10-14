using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>
/// Golden-set snapshot tests for <see cref="PayrollCalculator"/> (11.3.07-bordro.md "Test": monthly-salaried, full
/// worked month, Cash meal variant only). Domain-layer only - no DB, no Testcontainers. The LegislationParameter
/// values below mirror the seed in Sense.Modules.Leave's LegislationParameterSeed (Payroll.* keys, effective
/// 2026-01-01) so the expected numbers can be hand-derived and commented rather than treated as opaque constants.
/// </summary>
public sealed class PayrollCalculatorGoldenSetTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 15);

    private const int CalendarDaysInMonth = 30;

    /// <summary>Full worked month (no Attendance input yet - explicit user decision, faz3-durum-takibi.md).</summary>
    private const int WorkedDays = 30;

    private const decimal MinimumWageGross = 30_000.00m;
    private const decimal SgkFloor = 30_000.00m;
    private const decimal SgkCeiling = 225_000.00m;
    private const decimal SgkEmployeeRate = 0.14m;
    private const decimal UnemploymentEmployeeRate = 0.01m;
    private const decimal SgkEmployerRate = 0.205m;
    private const decimal UnemploymentEmployerRate = 0.02m;
    private const decimal SgkEmployerDiscountRate = 0.05m;
    private const decimal StampTaxRate = 0.00759m;
    private const decimal MealIncomeTaxDailyExemption = 300m;
    private const decimal MealSgkDailyExemption =  158m;

    /// <summary>Current draft (LegislationParameterSeed id 39): the meal+market card variant is NOT exempt.</summary>
    private const bool MealMarketCardExemptionApplies = false;

    /// <summary>GVK md.31 taslak aylık indirim tutarları (senseik-019 2b) - mali müşavirle doğrulanmalı.</summary>
    private const decimal DisabilityMonthlyTaxReductionDegree1 = 6900.00m;

    private const decimal DisabilityMonthlyTaxReductionDegree2 = 4000.00m;

    private const decimal DisabilityMonthlyTaxReductionDegree3 = 1700.00m;

    /// <summary>2026 draft brackets (15/20/27/35/40), same shape as the seeded Payroll.IncomeTax.Brackets JSON.</summary>
    private static readonly PayrollLegislationSnapshot Snapshot = new(
        [
            new TaxBracket(158_000m, 0.15m),
            new TaxBracket(330_000m, 0.20m),
            new TaxBracket(1_200_000m, 0.27m),
            new TaxBracket(4_300_000m, 0.35m),
            new TaxBracket(null, 0.40m),
        ],
        SgkFloor,
        SgkCeiling,
        SgkEmployeeRate,
        UnemploymentEmployeeRate,
        SgkEmployerRate,
        UnemploymentEmployerRate,
        SgkEmployerDiscountRate,
        StampTaxRate,
        MinimumWageGross,
        MealIncomeTaxDailyExemption,
        MealSgkDailyExemption,
        MealMarketCardExemptionApplies,
        DisabilityMonthlyTaxReductionDegree1,
        DisabilityMonthlyTaxReductionDegree2,
        DisabilityMonthlyTaxReductionDegree3,
        AsOf);

    [Fact]
    public void MidRangeSalary_NoMeal_ProducesFullTraceWithBracketBreakdown()
    {
        // Gross = 200,000: GV_BASE = 200,000 * 0.85 = 170,000, which crosses the 158,000 bracket boundary, so
        // INCOME_TAX spans two brackets (15% up to 158,000, 20% on the remaining 12,000) - exercises the breakdown.
        var input = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);

        var result = PayrollCalculator.Calculate(input, Snapshot);

        result.Steps.Select(s => s.Code).ShouldBe(
        [
            PayrollTraceStepCodes.Days, PayrollTraceStepCodes.Earnings, PayrollTraceStepCodes.SgkBase, PayrollTraceStepCodes.SgkEmployee,
            PayrollTraceStepCodes.UnempEmployee, PayrollTraceStepCodes.GvBase, PayrollTraceStepCodes.DisabilityReduction, PayrollTraceStepCodes.CumulativeBase,
            PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.MinWageExemptionGv, PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.MinWageExemptionDv,
            PayrollTraceStepCodes.Deductions, PayrollTraceStepCodes.Net, PayrollTraceStepCodes.EmployerSgk, PayrollTraceStepCodes.EmployerDiscount,
            PayrollTraceStepCodes.EmployerUnemp, PayrollTraceStepCodes.EmployerCost,
        ]);

        result.Step(PayrollTraceStepCodes.Days).Output.ShouldBe(30m);
        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(200_000m);
        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(200_000m); // below the 225,000 ceiling, no clamp
        result.Step(PayrollTraceStepCodes.SgkEmployee).Output.ShouldBe(28_000m); // 200,000 * 14%
        result.Step(PayrollTraceStepCodes.UnempEmployee).Output.ShouldBe(2_000m); // 200,000 * 1%
        result.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(170_000m); // 200,000 - 28,000 - 2,000
        result.Step(PayrollTraceStepCodes.CumulativeBase).Output.ShouldBe(170_000m); // first month, prior cumulative = 0

        // INCOME_TAX (cumulative bracket method): 158,000*15% + 12,000*20% = 23,700 + 2,400 = 26,100.
        var incomeTaxStep = result.Step(PayrollTraceStepCodes.IncomeTax);
        incomeTaxStep.Output.ShouldBe(26_100m);
        incomeTaxStep.Inputs["bracket1.amount"].ShouldBe("158000");
        incomeTaxStep.Inputs["bracket1.rate"].ShouldBe("0.15");
        incomeTaxStep.Inputs["bracket2.amount"].ShouldBe("12000");
        incomeTaxStep.Inputs["bracket2.rate"].ShouldBe("0.2");

        // MINWAGE_EXEMPTION_GV: minimum wage's own GV base (30,000 * 0.85 = 25,500) taxed at the first bracket
        // rate (15%) = 3,825 - the amount exempted from this month's INCOME_TAX (not the after-exemption tax itself).
        result.Step(PayrollTraceStepCodes.MinWageExemptionGv).Output.ShouldBe(3_825m);
        result.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(1_518m); // 200,000 * 0.759%
        result.Step(PayrollTraceStepCodes.MinWageExemptionDv).Output.ShouldBe(227.70m); // 30,000 * 0.759%
        result.Step(PayrollTraceStepCodes.Deductions).Output.ShouldBe(0m); // no avans/icra/nafaka/sendika/BES this slice (P3/P5)

        // NET = 200,000 - 28,000 - 2,000 - (26,100 - 3,825) - (1,518 - 227.70) + 0 (no meal) = 146,434.70.
        result.Step(PayrollTraceStepCodes.Net).Output.ShouldBe(146_434.70m);
        result.NetAmount.ShouldBe(146_434.70m);

        result.Step(PayrollTraceStepCodes.EmployerSgk).Output.ShouldBe(41_000m); // 200,000 * 20.5%
        result.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(0m); // 5-point incentive not applied this slice (TODO P2+)
        result.Step(PayrollTraceStepCodes.EmployerUnemp).Output.ShouldBe(4_000m); // 200,000 * 2%
        result.Step(PayrollTraceStepCodes.EmployerCost).Output.ShouldBe(245_000m); // 200,000 + 41,000 - 0 + 4,000
        result.GrossAmount.ShouldBe(200_000m);
        result.EmployerCost.ShouldBe(245_000m);
    }

    [Fact]
    public void MinimumWageGross_IncomeTaxAndStampTax_NetToZeroAfterMinWageExemption()
    {
        // An employee earning exactly the minimum wage: their own INCOME_TAX/STAMP_TAX IS the minimum wage's tax/duty,
        // so both must be fully exempted - the golden-set case every real Turkish payroll engine must get right.
        var input = new PayrollCalculationInput(MinimumWageGross, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);

        var result = PayrollCalculator.Calculate(input, Snapshot);

        var incomeTaxBeforeExemption = result.Step(PayrollTraceStepCodes.IncomeTax).Output;
        var incomeTaxExemption = result.Step(PayrollTraceStepCodes.MinWageExemptionGv).Output;
        (incomeTaxBeforeExemption - incomeTaxExemption).ShouldBe(0m);

        var stampTaxBeforeExemption = result.Step(PayrollTraceStepCodes.StampTax).Output;
        var stampTaxExemption = result.Step(PayrollTraceStepCodes.MinWageExemptionDv).Output;
        (stampTaxBeforeExemption - stampTaxExemption).ShouldBe(0m);

        // NET = gross - SGK_EMPLOYEE(14%) - UNEMP_EMPLOYEE(1%) - 0 - 0 = 30,000 * 0.85 = 25,500.
        result.NetAmount.ShouldBe(25_500m);
    }

    [Fact]
    public void MidRangeSalary_CashMealWithinExemption_LeavesBasesUnchangedButIncreasesNet()
    {
        // 150/day is below BOTH exemptions supplied by this fixture (SGK 158/day and GV 300/day).
        const decimal mealDailyAmount = 150m;
        var noMealInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var withMealInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.Cash, mealDailyAmount, 0m, AsOf);

        var noMeal = PayrollCalculator.Calculate(noMealInput, Snapshot);
        var withMeal = PayrollCalculator.Calculate(withMealInput, Snapshot);

        withMeal.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.SgkBase).Output);
        withMeal.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.GvBase).Output);
        withMeal.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.IncomeTax).Output);
        withMeal.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.StampTax).Output);

        // Fully-exempt meal cash is paid net, on top of the no-meal NET: 150 * 30 = 4,500.
        var mealTotal = mealDailyAmount * WorkedDays;
        withMeal.NetAmount.ShouldBe(noMeal.NetAmount + mealTotal);
        withMeal.NetAmount.ShouldBe(150_934.70m);
    }

    [Fact]
    public void InKindMeal_IsInformationalOnly_LeavesBasesAndNetUnchanged()
    {
        // Ayni yemek (11.3.07-bordro.md "ayni → SGK ve GV matrahı dışında, tutarsız, bilgi kalemi"): the employer
        // provides the meal directly, so no cash reaches the employee and the daily amount does not move any base.
        var noMealInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var inKindInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.InKind, 200m, 0m, AsOf);

        var noMeal = PayrollCalculator.Calculate(noMealInput, Snapshot);
        var inKind = PayrollCalculator.Calculate(inKindInput, Snapshot);

        inKind.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.SgkBase).Output);
        inKind.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.GvBase).Output);
        inKind.NetAmount.ShouldBe(noMeal.NetAmount);
    }

    [Fact]
    public void MealCardOnly_WithinExemption_BehavesLikeCash()
    {
        // "nakit ve yalnız yemek kartı" share the same exemption rule (11.3.07-bordro.md) - same numbers as the
        // existing Cash-within-exemption case, just paid via card instead of cash.
        const decimal mealDailyAmount = 200m;
        var cashInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.Cash, mealDailyAmount, 0m, AsOf);
        var cardInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.MealCardOnly, mealDailyAmount, 0m, AsOf);

        var cash = PayrollCalculator.Calculate(cashInput, Snapshot);
        var card = PayrollCalculator.Calculate(cardInput, Snapshot);

        card.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(cash.Step(PayrollTraceStepCodes.SgkBase).Output);
        card.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(cash.Step(PayrollTraceStepCodes.GvBase).Output);
        card.NetAmount.ShouldBe(cash.NetAmount);
    }

    [Fact]
    public void MealAndMarketCard_ExemptionNotApplied_EntersBothBasesButStillPaidInNet()
    {
        // Yemek+market kartı, mevcut taslak parametreyle (Meal.MarketCard.ExemptionApplies=false, id 39): tamamı SGK
        // ve GV matrahına girer (istisna yok) ama yine de net'e nakit yemek gibi eklenir (kart yüklemesi de bir ödeme).
        const decimal mealDailyAmount = 200m;
        var mealTotal = mealDailyAmount * WorkedDays; // 6,000
        var noMealInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var marketCardInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.MealAndMarketCard, mealDailyAmount, 0m, AsOf);

        var noMeal = PayrollCalculator.Calculate(noMealInput, Snapshot);
        var marketCard = PayrollCalculator.Calculate(marketCardInput, Snapshot);

        // SGK_BASE = clamp(200,000 + 6,000, floor, ceiling) = 206,000 (no exemption, full amount is excess).
        marketCard.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.SgkBase).Output + mealTotal);
        marketCard.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBeGreaterThan(noMeal.Step(PayrollTraceStepCodes.GvBase).Output);
        marketCard.NetAmount.ShouldBeGreaterThan(noMeal.NetAmount);
    }

    [Fact]
    public void UnknownMealVariant_Throws()
    {
        var input = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, (MealBenefitVariant)99, 100m, 0m, AsOf);

        Should.Throw<NotSupportedException>(() => PayrollCalculator.Calculate(input, Snapshot));
    }

    [Fact]
    public void UnknownDisabilityDegree_Throws()
    {
        var input = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf, DisabilityDegree: 4);

        Should.Throw<NotSupportedException>(() => PayrollCalculator.Calculate(input, Snapshot));
    }

    /// <summary>senseik-019 2b golden-set senaryosu: engelli (2. derece) çalışan - GVK md.31 matrah indirimi
    /// MINWAGE_EXEMPTION_GV'den önce uygulanır, dolayısıyla marjinal dilim oranında (burada %20) vergi tasarrufu sağlar.</summary>
    [Fact]
    public void DisabledEmployee_SecondDegree_ReducesGvBaseAndIncomeTaxBeforeMinWageExemption()
    {
        var noDisabilityInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var disabledInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf, DisabilityDegree: 2);

        var noDisability = PayrollCalculator.Calculate(noDisabilityInput, Snapshot);
        var disabled = PayrollCalculator.Calculate(disabledInput, Snapshot);

        disabled.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(noDisability.Step(PayrollTraceStepCodes.GvBase).Output); // GV_BASE'in kendisi değişmez
        disabled.Step(PayrollTraceStepCodes.DisabilityReduction).Output.ShouldBe(DisabilityMonthlyTaxReductionDegree2);

        // CUMULATIVE_BASE = 170,000 - 4,000 = 166,000; INCOME_TAX = 158,000*15% + 8,000*20% = 23,700 + 1,600 = 25,300.
        disabled.Step(PayrollTraceStepCodes.CumulativeBase).Output.ShouldBe(166_000m);
        disabled.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(25_300m);

        // %20 marjinal dilimde 4,000 TL matrah indirimi = 800 TL vergi tasarrufu, doğrudan NET'e yansır.
        disabled.NetAmount.ShouldBe(noDisability.NetAmount + 800m);
    }

    /// <summary>senseik-019 2a golden-set senaryosu: teşvikli işveren (5510 md.81) - yalnızca işveren maliyetini
    /// etkiler, çalışanın net ücretine dokunmaz (SGK işçi payı teşvikten etkilenmiyor).</summary>
    [Fact]
    public void IncentiveEligibleEmployer_AppliesFivePointSgkDiscount_ReducesEmployerCostOnlyNotEmployeeNet()
    {
        var noIncentiveInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var incentiveInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf, IncentiveEligible: true);

        var noIncentive = PayrollCalculator.Calculate(noIncentiveInput, Snapshot);
        var incentive = PayrollCalculator.Calculate(incentiveInput, Snapshot);

        // EMPLOYER_DISCOUNT = 200,000 * 5% = 10,000; efektif işveren SGK oranı %20,5 -> %15,5.
        incentive.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(10_000m);
        incentive.EmployerCost.ShouldBe(noIncentive.EmployerCost - 10_000m);
        incentive.EmployerCost.ShouldBe(235_000m);
        incentive.NetAmount.ShouldBe(noIncentive.NetAmount); // çalışan tarafı etkilenmez
    }

    /// <summary>senseik-019 2d golden-set senaryosu: fazla mesaili ay. OvertimePayCalculator'ın ürettiği tutar
    /// maktu ücrete eklenir ve SGK/GV/damga matrahlarının tamamına (maktu ücret gibi) dahil olur. 270 saatlik
    /// yıllık sayaç ve saatlerin gerçek Attendance verisinden 25/50 kategorisine ayrıştırılması senseik-012'nin
    /// sorumluluğu (bkz. handoff) - burada yalnızca TL akışı doğrulanıyor.</summary>
    [Fact]
    public void OvertimeMonth_AdditionalTaxableEarningsFlowsThroughGrossSgkAndNet()
    {
        var overtimePay = OvertimePayCalculator.Calculate(200_000m, overtimeHours25: 10m, overtimeHours50: 5m);
        overtimePay.ShouldBeGreaterThan(0m);

        var noOvertimeInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var overtimeInput = new PayrollCalculationInput(
            200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf, AdditionalTaxableEarnings: overtimePay);

        var noOvertime = PayrollCalculator.Calculate(noOvertimeInput, Snapshot);
        var overtime = PayrollCalculator.Calculate(overtimeInput, Snapshot);

        overtime.GrossAmount.ShouldBe(noOvertime.GrossAmount + overtimePay);
        overtime.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(noOvertime.Step(PayrollTraceStepCodes.SgkBase).Output + overtimePay);
        overtime.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBeGreaterThan(noOvertime.Step(PayrollTraceStepCodes.StampTax).Output);
        overtime.NetAmount.ShouldBeGreaterThan(noOvertime.NetAmount);
    }

    /// <summary>senseik-019 2a golden-set senaryosu: "yıl içinde işe giren çalışan" için doğru dilim/matrah devri.
    /// 2. ayın PriorCumulativeGvBase'i 1. ayın CUMULATIVE_BASE çıktısını devralır (Application katmanında
    /// PayrollPeriodCalculationService'in önceki dönemin Payslip'inden okuduğu değer - bkz. handoff); aynı brüt
    /// tekrarlansa bile kümülatif matrah 158.000 dilim sınırını aşarak 2. ayda daha yüksek oranlı vergiye geçer.</summary>
    [Fact]
    public void MidYearProgression_CumulativeGvBaseCarriesAcrossMonths_CrossesBracketBoundary()
    {
        var month1Input = new PayrollCalculationInput(100_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var month1 = PayrollCalculator.Calculate(month1Input, Snapshot);

        month1.Step(PayrollTraceStepCodes.CumulativeBase).Output.ShouldBe(85_000m); // 100,000 * 0.85
        month1.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(12_750m); // 85,000 * %15

        var priorCumulative = month1.Step(PayrollTraceStepCodes.CumulativeBase).Output;
        var month2Input = new PayrollCalculationInput(100_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, priorCumulative, AsOf.AddMonths(1));
        var month2 = PayrollCalculator.Calculate(month2Input, Snapshot);

        month2.Step(PayrollTraceStepCodes.CumulativeBase).Output.ShouldBe(170_000m); // 85,000 + 85,000, 158,000 sınırını aşıyor
        // 73,000 kalan %15 diliminde + 12,000 %20 diliminde = 10,950 + 2,400 = 13,350 (1. aydan yüksek, aynı brüte rağmen).
        month2.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(13_350m);
        month2.NetAmount.ShouldBeLessThan(month1.NetAmount);
    }

    /// <summary>senseik-019 2c golden-set senaryosu: icra + nafaka kesintili ve açık avanslı çalışan. Nafaka önce ve
    /// sınırsız (kalan nete kadar) kesilir, icra net ücretin (kesinti öncesi) %25'i havuzuyla sınırlıdır, avans kalan
    /// nete kadar mahsup edilir. BASİTLEŞTİRME - SMMM/hukuk onayı gerekli (bkz. GarnishmentCalculator doc, handoff).</summary>
    [Fact]
    public void GarnishmentAndAdvance_AppliesNafakaFirstThenIcraCapThenAdvance()
    {
        var nafakaOrderId = Guid.NewGuid();
        var icraOrderId = Guid.NewGuid();
        var orders = new List<GarnishmentDeductionInput>
        {
            new(nafakaOrderId, GarnishmentType.Nafaka, MonthlyInstallmentAmount: 2_000m, RemainingBalance: 10_000m),
            new(icraOrderId, GarnishmentType.Icra, MonthlyInstallmentAmount: 50_000m, RemainingBalance: 200_000m),
        };
        var input = new PayrollCalculationInput(
            200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf,
            AdvanceDeductionTotal: 5_000m, GarnishmentOrders: orders);

        var result = PayrollCalculator.Calculate(input, Snapshot);

        // netBeforeDeductions = 146,434.70 (bkz. MidRangeSalary_NoMeal testi, kesinti yokken aynı Net). Nafaka tam
        // (2,000) kesilir; icra %25 tavanına (36,608.68) kadar kesilir; avans kalan nete kadar (5,000) düşer.
        result.Step(PayrollTraceStepCodes.Deductions).Output.ShouldBe(43_608.68m);
        result.NetAmount.ShouldBe(102_826.02m);
        result.GarnishmentApplied.ShouldContain(a => a.OrderId == nafakaOrderId && a.Amount == 2_000m);
        result.GarnishmentApplied.ShouldContain(a => a.OrderId == icraOrderId && a.Amount == 36_608.68m);
    }

    /// <summary>11.3.07-bordro.md #58 "Sendika / TİS" golden-set senaryosu: aidat kesintisi NET'ten düşülür,
    /// SGK/GV matrahlarına hiç dokunmaz.</summary>
    [Fact]
    public void UnionDues_DeductedFromNet_ButNeverTouchesSgkGvBases()
    {
        // 11.3.07-bordro.md #58 "Sendika / TİS": dues are a post-tax deduction, resolved by the Application layer
        // (UnionDuesCalculator) and passed in as a plain amount - the calculator itself just wires DEDUCTIONS into NET.
        var noDuesInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var withDuesInput = noDuesInput with { UnionDuesAmount = 2_000m };

        var noDues = PayrollCalculator.Calculate(noDuesInput, Snapshot);
        var withDues = PayrollCalculator.Calculate(withDuesInput, Snapshot);

        withDues.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(noDues.Step(PayrollTraceStepCodes.SgkBase).Output);
        withDues.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(noDues.Step(PayrollTraceStepCodes.GvBase).Output);
        withDues.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(noDues.Step(PayrollTraceStepCodes.IncomeTax).Output);
        withDues.Step(PayrollTraceStepCodes.Deductions).Output.ShouldBe(2_000m);
        withDues.NetAmount.ShouldBe(noDues.NetAmount - 2_000m);
        withDues.GrossAmount.ShouldBe(noDues.GrossAmount); // dues never reduce GROSS/EARNINGS
    }

    // ---- PAY-FORMULA-3: formüllü ödeme türleri (PayItems). Aşağıdaki senaryolar yalnız kalemli hesabı sınar; kalemsiz hesap yukarıdaki testlerle aynen korunur. ----

    private static PayrollCalculationInput PlainInput(decimal salary = 200_000m) =>
        new(salary, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);

    private static PayItemEarning Item(
        decimal amount, bool sgk = true, bool incomeTax = true, bool stamp = true, string code = "PRIM", decimal? netTarget = null) =>
        new(code, "Test kalemi", amount, sgk, incomeTax, stamp, "deger", new Dictionary<string, decimal> { ["deger"] = amount }, netTarget);

    [Fact]
    public void PayItems_NullOrEmpty_LeaveTheTraceExactlyAsBefore()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var plain = PayrollCalculator.Calculate(PlainInput(), Snapshot);
        var withEmpty = PayrollCalculator.Calculate(PlainInput() with { PayItems = [] }, Snapshot);

        var plainJson = System.Text.Json.JsonSerializer.Serialize(plain.Steps, options);
        System.Text.Json.JsonSerializer.Serialize(withEmpty.Steps, options).ShouldBe(plainJson);

        // Hiçbir adım kalem alanı taşımaz: ne PAY_ITEM adımı, ne payItems* girdisi, ne "payItem" JSON alanı.
        plain.Steps.ShouldNotContain(s => s.Code == PayrollTraceStepCodes.PayItem);
        plainJson.ShouldNotContain("payItem");
        plain.Step(PayrollTraceStepCodes.Earnings).Inputs.Keys.Order().ShouldBe(["additionalTaxableEarnings", "baseSalary", "grossTotal"]);
        plain.Step(PayrollTraceStepCodes.SgkBase).Inputs.Keys.Order().ShouldBe(["gross", "mealSgkExcess", "sgkCeiling", "sgkFloor"]);
        plain.Step(PayrollTraceStepCodes.GvBase).Inputs.Keys.Order().ShouldBe(["gross", "mealGvExcess", "sgkEmployee", "unempEmployee"]);
        plain.Step(PayrollTraceStepCodes.StampTax).Inputs.Keys.Order().ShouldBe(["gross", "rate"]);
        plain.Step(PayrollTraceStepCodes.StampTax).Formula.ShouldBe("gross * damga vergisi oranı");
    }

    [Fact]
    public void FullyTaxablePayItem_GivesTheSameFiguresAsAdditionalTaxableEarnings()
    {
        var viaItem = PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(10_000m)] }, Snapshot);
        var viaAdditional = PayrollCalculator.Calculate(PlainInput() with { AdditionalTaxableEarnings = 10_000m }, Snapshot);

        viaItem.GrossAmount.ShouldBe(viaAdditional.GrossAmount);
        viaItem.NetAmount.ShouldBe(viaAdditional.NetAmount);
        viaItem.EmployerCost.ShouldBe(viaAdditional.EmployerCost);
        foreach (var code in new[]
        {
            PayrollTraceStepCodes.Earnings, PayrollTraceStepCodes.SgkBase, PayrollTraceStepCodes.SgkEmployee, PayrollTraceStepCodes.GvBase,
            PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.Net, PayrollTraceStepCodes.EmployerCost,
        })
        {
            viaItem.Step(code).Output.ShouldBe(viaAdditional.Step(code).Output, code);
        }

        viaItem.GrossAmount.ShouldBe(210_000m);
    }

    [Fact]
    public void PayItemSteps_ComeBeforeEarnings_AndRecordFormulaVariablesAndFlags()
    {
        var item = new PayItemEarning(
            "YEMEK", "Yemek yardımı", 1_500m, SgkSubject: false, IncomeTaxSubject: true, StampTaxSubject: true,
            "min(asgari_ucret * 0.05; deger)",
            new Dictionary<string, decimal> { ["asgari_ucret"] = 30_000m, ["deger"] = 1_500m });

        var result = PayrollCalculator.Calculate(PlainInput() with { PayItems = [item] }, Snapshot);

        result.Steps.Select(s => s.Code).Take(3).ShouldBe([PayrollTraceStepCodes.Days, PayrollTraceStepCodes.PayItem, PayrollTraceStepCodes.Earnings]);
        result.Steps.Select(s => s.Order).ShouldBe(Enumerable.Range(1, result.Steps.Count).ToList()); // sıra numaraları kesintisiz
        var step = result.Step(PayrollTraceStepCodes.Earnings);
        step.Inputs["payItemsTotal"].ShouldBe("1500");

        var payItemStep = result.Steps.Single(s => s.Code == PayrollTraceStepCodes.PayItem);
        payItemStep.PayItem.ShouldBe("YEMEK");
        payItemStep.Output.ShouldBe(1_500m);
        payItemStep.Formula.ShouldBe(item.Formula);
        payItemStep.Inputs["code"].ShouldBe("YEMEK");
        payItemStep.Inputs["amountBasis"].ShouldBe("gross");
        payItemStep.Inputs["sgkSubject"].ShouldBe("0");
        payItemStep.Inputs["incomeTaxSubject"].ShouldBe("1");
        payItemStep.Inputs["variable.asgari_ucret"].ShouldBe("30000");
        payItemStep.Inputs["variable.deger"].ShouldBe("1500");
        payItemStep.ParameterRefs.ShouldContain(r => r.Key == "Payroll.MinimumWage.Gross" && r.Value == 30_000m);
        result.Step(PayrollTraceStepCodes.SgkBase).Inputs["payItemsSgkExempt"].ShouldBe("1500");
        result.Step(PayrollTraceStepCodes.GvBase).Inputs["payItemsGvExempt"].ShouldBe("0");
        result.Step(PayrollTraceStepCodes.StampTax).Inputs["payItemsStampExempt"].ShouldBe("0");
    }

    [Fact]
    public void SgkExemptPayItem_JoinsGrossAndGvAndStamp_ButNotTheSgkBase()
    {
        var result = PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(10_000m, sgk: false)] }, Snapshot);

        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(210_000m);
        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(200_000m); // 210,000 - 10,000 (SGK'dan muaf)
        result.Step(PayrollTraceStepCodes.SgkEmployee).Output.ShouldBe(28_000m);
        result.Step(PayrollTraceStepCodes.UnempEmployee).Output.ShouldBe(2_000m);
        result.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(180_000m); // 210,000 - 28,000 - 2,000
        result.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(1_593.90m); // 210,000 * 0.759%
    }

    [Fact]
    public void IncomeTaxExemptPayItem_LeavesTheGvBaseButStaysInSgkAndStamp()
    {
        var result = PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(10_000m, incomeTax: false)] }, Snapshot);

        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(210_000m);
        result.Step(PayrollTraceStepCodes.SgkEmployee).Output.ShouldBe(29_400m); // 210,000 * 14%
        result.Step(PayrollTraceStepCodes.UnempEmployee).Output.ShouldBe(2_100m);
        result.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(168_500m); // 210,000 - 10,000 - 29,400 - 2,100
        result.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(1_593.90m);
    }

    [Fact]
    public void StampExemptPayItem_LeavesTheStampBaseButStaysInSgkAndGv()
    {
        var result = PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(10_000m, stamp: false)] }, Snapshot);

        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(210_000m);
        result.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(178_500m); // 210,000 - 29,400 - 2,100
        result.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(1_518m); // 200,000 * 0.759%
    }

    [Fact]
    public void FullyExemptPayItem_AddsItsAmountToNetAndEmployerCostWithoutTouchingAnyBase()
    {
        var plain = PayrollCalculator.Calculate(PlainInput(), Snapshot);
        var result = PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(10_000m, sgk: false, incomeTax: false, stamp: false)] }, Snapshot);

        foreach (var code in new[]
        {
            PayrollTraceStepCodes.SgkBase, PayrollTraceStepCodes.SgkEmployee, PayrollTraceStepCodes.GvBase, PayrollTraceStepCodes.IncomeTax,
            PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.EmployerSgk, PayrollTraceStepCodes.EmployerUnemp,
        })
        {
            result.Step(code).Output.ShouldBe(plain.Step(code).Output, code);
        }

        result.GrossAmount.ShouldBe(plain.GrossAmount + 10_000m);
        result.NetAmount.ShouldBe(plain.NetAmount + 10_000m);
        result.EmployerCost.ShouldBe(plain.EmployerCost + 10_000m);
    }

    [Fact]
    public void ExemptPayItem_NeverPullsTheSgkBaseBelowTheFloor()
    {
        // 30,000 asgari ücretli çalışan + SGK'dan muaf 5,000: brüt 35,000, SGK matrahı 30,000 (taban).
        var result = PayrollCalculator.Calculate(PlainInput(30_000m) with { PayItems = [Item(5_000m, sgk: false)] }, Snapshot);

        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(30_000m);
        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(35_000m);
    }

    [Fact]
    public void MultiplePayItems_AreSummedAndEachGetsItsOwnStep()
    {
        var result = PayrollCalculator.Calculate(
            PlainInput() with { PayItems = [Item(1_000m, code: "A"), Item(2_000m, sgk: false, code: "B")] }, Snapshot);

        result.Steps.Where(s => s.Code == PayrollTraceStepCodes.PayItem).Select(s => s.PayItem).ShouldBe(["A", "B"]);
        result.Step(PayrollTraceStepCodes.Earnings).Inputs["payItemsTotal"].ShouldBe("3000");
        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(203_000m);
        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(201_000m);
    }

    [Fact]
    public void PayItemOnAProratedMonth_UsesTheProratedBaseButTheItemAsGiven()
    {
        // Kıst ay (15 gün): aylık ücret yarıya iner; kalem tutarı (formülün işi) olduğu gibi eklenir.
        var input = PlainInput(60_000m) with { ProratedPaidDays = 15, PayItems = [Item(2_000m)] };

        var result = PayrollCalculator.Calculate(input, Snapshot);

        PayrollCalculator.BaseSalary(input).ShouldBe(30_000m);
        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(32_000m);
    }

    [Fact]
    public void PayItemOnADailyWageEmployee_AddsToTheDailyWagePeriodGross()
    {
        var input = PlainInput(60_000m) with { Wage = new PeriodWage(WageBasis.Daily, 2_000m, 25), ProratedPaidDays = 25, PayItems = [Item(1_500m)] };

        var result = PayrollCalculator.Calculate(input, Snapshot);

        PayrollCalculator.BaseSalary(input).ShouldBe(50_000m); // 2,000 x 25
        result.Step(PayrollTraceStepCodes.Earnings).Output.ShouldBe(51_500m);
    }

    [Fact]
    public void BaseSalary_MatchesTheEarningsStepsBaseSalaryInEveryShape()
    {
        PayrollCalculationInput[] inputs =
        [
            PlainInput(),
            PlainInput(45_000m) with { ProratedPaidDays = 17 },
            PlainInput(60_000m) with { Wage = new PeriodWage(WageBasis.Hourly, 150m, 20), ProratedPaidDays = 20 },
        ];

        foreach (var input in inputs)
        {
            var earnings = PayrollCalculator.Calculate(input, Snapshot).Step(PayrollTraceStepCodes.Earnings);
            decimal.Parse(earnings.Inputs["baseSalary"], System.Globalization.CultureInfo.InvariantCulture).ShouldBe(PayrollCalculator.BaseSalary(input));
        }
    }

    [Fact]
    public void NegativePayItemAmount_IsRejectedByTheEngine()
    {
        Should.Throw<Exception>(() => PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(-1m)] }, Snapshot));
    }

    [Fact]
    public void NetPayItem_GrossedUpWithTheSolver_LeavesTheEmployeeTheTargetNetWithinOneKurus()
    {
        const decimal netTarget = 10_000m;
        var baseline = PayrollCalculator.Calculate(PlainInput(), Snapshot);

        var grossUp = NetToGrossSolver.SolveExtra(netTarget, extra => PayrollCalculator.Calculate(PlainInput() with { AdditionalTaxableEarnings = extra }, Snapshot));
        var result = PayrollCalculator.Calculate(PlainInput() with { PayItems = [Item(grossUp, netTarget: netTarget)] }, Snapshot);

        grossUp.ShouldBeGreaterThan(netTarget);
        Math.Abs(result.NetAmount - baseline.NetAmount - netTarget).ShouldBeLessThanOrEqualTo(0.01m);
        result.Steps.Single(s => s.Code == PayrollTraceStepCodes.PayItem).Inputs["netTarget"].ShouldBe("10000");
        result.Steps.Single(s => s.Code == PayrollTraceStepCodes.PayItem).Inputs["amountBasis"].ShouldBe("net");
    }
}
