using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Kıst (partial-month) proration: 30-day basis paid days and the <see cref="PayrollCalculator"/> scaling of
/// gross, SGK taban/tavan and asgari ücret istisnaları.</summary>
public sealed class EmploymentProrationTests
{
    private static readonly DateOnly SepStart = new(2026, 9, 1);
    private static readonly DateOnly SepEnd = new(2026, 9, 30);

    [Theory]
    [InlineData(null, null, 30)] // no dates known: full month
    [InlineData("2026-01-01", null, 30)] // hired long ago
    [InlineData("2026-09-01", null, 30)] // hired on the first
    [InlineData("2026-09-16", null, 15)] // 16..30
    [InlineData("2026-09-30", null, 1)]
    [InlineData("2026-10-01", null, 0)] // not yet hired
    [InlineData(null, "2026-08-31", 0)] // left before the period
    [InlineData(null, "2026-09-10", 10)]
    [InlineData(null, "2026-09-30", 30)] // left on the last day: full month
    [InlineData("2026-09-06", "2026-09-15", 10)] // hired and left within the month
    public void PaidDays_UsesThe30DayBasis(string? hire, string? termination, int expected)
    {
        var result = EmploymentProration.PaidDays(SepStart, SepEnd, Parse(hire), Parse(termination));

        result.ShouldBe(expected);
    }

    [Fact]
    public void PaidDays_TreatsAThirtyOneDayMonthAsThirty_AndAHireOnThe31stAsOneDay()
    {
        var jan = (Start: new DateOnly(2026, 1, 1), End: new DateOnly(2026, 1, 31));

        EmploymentProration.PaidDays(jan.Start, jan.End, new DateOnly(2026, 1, 31), null).ShouldBe(1);
        EmploymentProration.PaidDays(jan.Start, jan.End, null, new DateOnly(2026, 1, 31)).ShouldBe(30);
    }

    [Fact]
    public void PaidDays_TreatsFebruaryAsThirty_WhenTheEmployeeWorksTheWholeMonth()
    {
        EmploymentProration.PaidDays(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), null, null).ShouldBe(30);
        EmploymentProration.PaidDays(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 15), null).ShouldBe(16);
    }

    [Fact]
    public void Calculate_WithProratedPaidDays_ScalesGrossSgkFloorAndMinimumWageExemptions()
    {
        var snapshot = Snapshot();
        var full = PayrollCalculator.Calculate(Input(60_000m, prorated: null), snapshot);
        var half = PayrollCalculator.Calculate(Input(60_000m, prorated: 15), snapshot);

        half.GrossAmount.ShouldBe(30_000m); // 60,000 * 15 / 30
        half.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(30_000m);
        half.Step(PayrollTraceStepCodes.SgkEmployee).Output.ShouldBe(4_200m); // 30,000 * 14%
        half.Step(PayrollTraceStepCodes.MinWageExemptionGv).Inputs["minimumWageGross"].ShouldBe("15000"); // 30,000 * 15/30
        half.Step(PayrollTraceStepCodes.MinWageExemptionDv).Inputs["minimumWageGross"].ShouldBe("15000");
        half.NetAmount.ShouldBeLessThan(full.NetAmount);
        full.GrossAmount.ShouldBe(60_000m);
    }

    [Fact]
    public void Calculate_WithProratedPaidDays_DoesNotInflateSgkBaseUpToTheFullMonthFloor()
    {
        // 30,000 * 10 / 30 = 10,000 gross: below the FULL taban (30,000) but above the prorated one (10,000).
        var result = PayrollCalculator.Calculate(Input(30_000m, prorated: 10), Snapshot());

        result.GrossAmount.ShouldBe(10_000m);
        result.Step(PayrollTraceStepCodes.SgkBase).Output.ShouldBe(10_000m);
    }

    [Fact]
    public void Calculate_WithoutProration_MatchesAFullThirtyDayInput()
    {
        var withoutValue = PayrollCalculator.Calculate(Input(75_000m, prorated: null), Snapshot());
        var withThirty = PayrollCalculator.Calculate(Input(75_000m, prorated: 30), Snapshot());

        withThirty.NetAmount.ShouldBe(withoutValue.NetAmount);
        withThirty.GrossAmount.ShouldBe(withoutValue.GrossAmount);
    }

    private static DateOnly? Parse(string? value) => value is null ? null : DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static PayrollCalculationInput Input(decimal gross, int? prorated) => new(
        gross, WorkedDays: 30, CalendarDaysInMonth: 30, MealBenefitVariant.None, 0m, 0m, SepStart, ProratedPaidDays: prorated);

    private static PayrollLegislationSnapshot Snapshot() => new(
        [
            new TaxBracket(158_000m, 0.15m),
            new TaxBracket(330_000m, 0.20m),
            new TaxBracket(1_200_000m, 0.27m),
            new TaxBracket(4_300_000m, 0.35m),
            new TaxBracket(null, 0.40m),
        ],
        SgkFloor: 30_000m, SgkCeiling: 225_000m, SgkEmployeeRate: 0.14m, UnemploymentEmployeeRate: 0.01m,
        SgkEmployerRate: 0.205m, UnemploymentEmployerRate: 0.02m, SgkEmployerDiscountRate: 0.05m, StampTaxRate: 0.00759m,
        MinimumWageGross: 30_000m, MealIncomeTaxDailyExemption: 240m, MealSgkDailyExemptionRate: 0.2365m,
        MealMarketCardExemptionApplies: false, DisabilityMonthlyTaxReductionDegree1: 6_900m,
        DisabilityMonthlyTaxReductionDegree2: 4_000m, DisabilityMonthlyTaxReductionDegree3: 1_700m, AsOf: SepStart);
}

/// <summary>QA teslim testi bulgu 7: hand-checked 2026 minimum-wage sanity check with the verified statutory values.</summary>
public sealed class MinimumWage2026Tests
{
    [Fact]
    public void GrossMinimumWage33030_YieldsTheOfficialNet28075_50()
    {
        var snapshot = new PayrollLegislationSnapshot(
            [
                new TaxBracket(190_000m, 0.15m), new TaxBracket(400_000m, 0.20m), new TaxBracket(1_500_000m, 0.27m),
                new TaxBracket(5_300_000m, 0.35m), new TaxBracket(null, 0.40m),
            ],
            SgkFloor: 33_030m, SgkCeiling: 247_725m, SgkEmployeeRate: 0.14m, UnemploymentEmployeeRate: 0.01m,
            SgkEmployerRate: 0.205m, UnemploymentEmployerRate: 0.02m, SgkEmployerDiscountRate: 0.05m, StampTaxRate: 0.00759m,
            MinimumWageGross: 33_030m, MealIncomeTaxDailyExemption: 240m, MealSgkDailyExemptionRate: 0.2365m,
            MealMarketCardExemptionApplies: false, DisabilityMonthlyTaxReductionDegree1: 6_900m,
            DisabilityMonthlyTaxReductionDegree2: 4_000m, DisabilityMonthlyTaxReductionDegree3: 1_700m, AsOf: new DateOnly(2026, 1, 1));

        var result = PayrollCalculator.Calculate(
            new PayrollCalculationInput(33_030m, 30, 30, MealBenefitVariant.None, 0m, 0m, new DateOnly(2026, 1, 1)), snapshot);

        result.Step(PayrollTraceStepCodes.SgkEmployee).Output.ShouldBe(4_624.20m); // 33,030 * 14%
        result.Step(PayrollTraceStepCodes.UnempEmployee).Output.ShouldBe(330.30m); // 33,030 * 1%
        result.Step(PayrollTraceStepCodes.StampTax).Output.ShouldBe(250.70m); // 33,030 * 0.759%
        result.Step(PayrollTraceStepCodes.MinWageExemptionGv).Output.ShouldBe(4_211.33m); // 28,075.50 * 15%
        result.NetAmount.ShouldBe(28_075.50m);
    }
}
