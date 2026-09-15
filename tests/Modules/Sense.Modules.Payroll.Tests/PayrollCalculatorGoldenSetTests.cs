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
    private const decimal MealIncomeTaxDailyExemption = 240.00m;
    private const decimal MealSgkDailyExemptionRate = 0.2365m;

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
        MealSgkDailyExemptionRate,
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
            PayrollTraceStepCodes.UnempEmployee, PayrollTraceStepCodes.GvBase, PayrollTraceStepCodes.CumulativeBase, PayrollTraceStepCodes.IncomeTax,
            PayrollTraceStepCodes.MinWageExemptionGv, PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.MinWageExemptionDv, PayrollTraceStepCodes.Deductions,
            PayrollTraceStepCodes.Net, PayrollTraceStepCodes.EmployerSgk, PayrollTraceStepCodes.EmployerDiscount, PayrollTraceStepCodes.EmployerUnemp,
            PayrollTraceStepCodes.EmployerCost,
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
        // Same gross as the mid-range case. Meal cash = 200/day * 30 days = 6,000, comfortably inside both the SGK
        // exemption (23.65% * daily gross min wage(1,000) * 30 days = 7,095) and the GV exemption (240/day * 30 = 7,200)
        // - "fully within exemption" per the P1 golden-set scope (excess-above-exemption cases are not tested here).
        const decimal mealDailyAmount = 200m;
        var noMealInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.None, 0m, 0m, AsOf);
        var withMealInput = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.Cash, mealDailyAmount, 0m, AsOf);

        var noMeal = PayrollCalculator.Calculate(noMealInput, Snapshot);
        var withMeal = PayrollCalculator.Calculate(withMealInput, Snapshot);

        withMeal.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.SgkBase).Output);
        withMeal.Step(PayrollTraceStepCodes.GvBase).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.GvBase).Output);
        withMeal.Step(PayrollTraceStepCodes.IncomeTax).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.IncomeTax).Output);
        withMeal.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(noMeal.Step(PayrollTraceStepCodes.StampTax).Output);

        // Fully-exempt meal cash is paid net, on top of the no-meal NET: 200 * 30 = 6,000.
        var mealTotal = mealDailyAmount * WorkedDays;
        withMeal.NetAmount.ShouldBe(noMeal.NetAmount + mealTotal);
        withMeal.NetAmount.ShouldBe(152_434.70m);
    }

    [Fact]
    public void UnsupportedMealVariant_Throws()
    {
        // InKind/MealCardOnly/MealAndMarketCard are deferred to P2 (faz3-durum-takibi.md); Calculate must fail loudly
        // rather than silently mis-price them.
        var input = new PayrollCalculationInput(200_000m, WorkedDays, CalendarDaysInMonth, MealBenefitVariant.InKind, 100m, 0m, AsOf);

        Should.Throw<NotSupportedException>(() => PayrollCalculator.Calculate(input, Snapshot));
    }
}
