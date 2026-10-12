using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.PayItems;
using Sense.Modules.Payroll.Domain.PayItems.Formulas;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>PAY-FORMULA-3: pure helpers around the engine - variable values, formula evaluation, net gross-up allocation. No DB, no mocks.</summary>
public sealed class PayItemCalculationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateOnly AsOf = new(2026, 9, 1);

    private static readonly PayrollLegislationSnapshot Snapshot = new(
        [new TaxBracket(158_000m, 0.15m), new TaxBracket(330_000m, 0.20m), new TaxBracket(1_200_000m, 0.27m), new TaxBracket(4_300_000m, 0.35m), new TaxBracket(null, 0.40m)],
        30_000m, 225_000m, 0.14m, 0.01m, 0.205m, 0.02m, 0.05m, 0.00759m, 30_000m, 240m, 0.2365m, false, 6900m, 4000m, 1700m, AsOf);

    private static readonly PayItemVariableFacts NoFacts = new(0m, 0, 0, 0m);

    private static PayrollCalculationInput Input(decimal salary = 60_000m, int? proratedDays = null, PeriodWage? wage = null, decimal additional = 0m) =>
        new(salary, 30, 30, MealBenefitVariant.None, 0m, 0m, AsOf, AdditionalTaxableEarnings: additional, ProratedPaidDays: proratedDays, Wage: wage);

    private static IReadOnlyDictionary<string, decimal> Vars(PayrollCalculationInput input, PayItemVariableFacts? facts = null, decimal? value = null) =>
        PayItemVariables.Build(input, Snapshot, facts ?? NoFacts, value);

    private static PayItemDefinition Definition(string code, string formula, PayItemAmountBasis basis = PayItemAmountBasis.Gross)
    {
        var data = new PayItemDefinitionData(code + " adı", PayItemDirection.Earning, basis, true, true, true, formula, null, new DateOnly(2026, 1, 1), null);
        return PayItemDefinition.Create(TenantId, code, data).Value;
    }

    private static EmployeePayItemAssignment Assign(PayItemDefinition definition, decimal? value = null) =>
        EmployeePayItemAssignment.Create(TenantId, Guid.NewGuid(), definition, value, AsOf, null, new HashSet<DateOnly>()).Value;

    [Fact]
    public void Variables_FullMonthSalary_BrutAndWagesAndDays()
    {
        var v = Vars(Input(60_000m));

        v[PayItemVariableNames.Gross].ShouldBe(60_000m);
        v[PayItemVariableNames.MonthlyWage].ShouldBe(60_000m);
        v[PayItemVariableNames.DailyWage].ShouldBe(2_000m);
        v[PayItemVariableNames.HourlyWage].ShouldBe(60_000m / 225m);
        v[PayItemVariableNames.WorkedDays].ShouldBe(30m);
        v[PayItemVariableNames.PaidDays].ShouldBe(30m);
        v[PayItemVariableNames.CalendarDays].ShouldBe(30m);
        v[PayItemVariableNames.Month].ShouldBe(9m);
        v[PayItemVariableNames.MinimumWage].ShouldBe(30_000m);
        v[PayItemVariableNames.NetTarget].ShouldBe(0m);
        v.ContainsKey(PayItemVariableNames.Value).ShouldBeFalse();
        v.Keys.Order().ShouldBe(PayItemVariableNames.All.Where(n => n != PayItemVariableNames.Value).Order());
    }

    [Fact]
    public void Variables_BrutExcludesOvertimeAndOtherEarnings()
    {
        Vars(Input(60_000m, additional: 7_000m))[PayItemVariableNames.Gross].ShouldBe(60_000m);
    }

    [Fact]
    public void Variables_PartialMonth_BrutIsProratedAndPaidDaysAreTheKistDays()
    {
        var v = Vars(Input(60_000m, proratedDays: 15), new PayItemVariableFacts(0m, 2, 0, 0m));

        v[PayItemVariableNames.Gross].ShouldBe(30_000m);
        v[PayItemVariableNames.MonthlyWage].ShouldBe(60_000m);
        v[PayItemVariableNames.PaidDays].ShouldBe(15m);
        v[PayItemVariableNames.MissingDays].ShouldBe(2m);
    }

    [Fact]
    public void Variables_DailyWage_UsesTheUnitRateAndWagePaidDays()
    {
        var wage = new PeriodWage(WageBasis.Daily, 2_000m, 25);
        var v = Vars(Input(60_000m, proratedDays: 25, wage: wage));

        v[PayItemVariableNames.DailyWage].ShouldBe(2_000m);
        v[PayItemVariableNames.Gross].ShouldBe(50_000m); // 2,000 x 25
        v[PayItemVariableNames.PaidDays].ShouldBe(25m);
    }

    [Fact]
    public void Variables_HourlyWage_HourlyWageIsTheUnitRate()
    {
        var wage = new PeriodWage(WageBasis.Hourly, 200m, 30);
        var v = Vars(Input(200m * 225m, wage: wage));

        v[PayItemVariableNames.HourlyWage].ShouldBe(200m);
        v[PayItemVariableNames.DailyWage].ShouldBe(1_500m);
        v[PayItemVariableNames.Gross].ShouldBe(45_000m); // 200 x 7.5 x 30
    }

    [Fact]
    public void Variables_CarryNetTargetOvertimeHoursSeniorityAndValue()
    {
        var v = Vars(Input(), new PayItemVariableFacts(45_000m, 0, 6, 12.5m), value: 750m);

        v[PayItemVariableNames.NetTarget].ShouldBe(45_000m);
        v[PayItemVariableNames.OvertimeHours].ShouldBe(12.5m);
        v[PayItemVariableNames.SeniorityYears].ShouldBe(6m);
        v[PayItemVariableNames.Value].ShouldBe(750m);
    }

    [Theory]
    [InlineData("2020-03-10", "2026-09-30", 6)]
    [InlineData("2020-09-30", "2026-09-30", 6)]
    [InlineData("2020-10-01", "2026-09-30", 5)]
    [InlineData("2026-09-15", "2026-09-30", 0)]
    [InlineData("2027-01-01", "2026-09-30", 0)]
    public void SeniorityYears_CountsWholeYearsUpToThePeriodEnd(string hire, string periodEnd, int expected)
    {
        PayItemVariables.SeniorityYears(DateOnly.Parse(hire), DateOnly.Parse(periodEnd)).ShouldBe(expected);
    }

    [Fact]
    public void SeniorityYears_UnknownHireDate_IsZero()
    {
        PayItemVariables.SeniorityYears(null, new DateOnly(2026, 9, 30)).ShouldBe(0);
    }

    [Fact]
    public void Evaluate_RoundsToTwoDecimalsAwayFromZero_AndSortsByCode()
    {
        var b = Definition("B_KALEM", "deger / 3");
        var a = Definition("A_KALEM", "deger");

        var evaluation = PayItemEvaluator.Evaluate([(Assign(b, 100m), b), (Assign(a, 0.005m), a)], deger => Vars(Input(), value: deger));

        evaluation.Succeeded.ShouldBeTrue();
        evaluation.Items.Select(i => i.Code).ShouldBe(["A_KALEM", "B_KALEM"]);
        evaluation.Items[0].Amount.ShouldBe(0.01m); // 0.005 -> sıfırdan uzağa
        evaluation.Items[1].Amount.ShouldBe(33.33m);
        evaluation.Items[1].UsedVariables.ShouldBe(new Dictionary<string, decimal> { ["deger"] = 100m });
    }

    [Fact]
    public void Evaluate_NegativeResult_IsClampedToZeroAndReported()
    {
        var d = Definition("NEG", "brut - deger");

        var evaluation = PayItemEvaluator.Evaluate([(Assign(d, 70_000m), d)], deger => Vars(Input(60_000m), value: deger));

        evaluation.Succeeded.ShouldBeTrue();
        evaluation.Items.Single().Amount.ShouldBe(0m);
        evaluation.NegativeCodes.ShouldBe(["NEG"]);
    }

    [Fact]
    public void Evaluate_DivisionByZero_FailsWithTheItemCode()
    {
        var ok = Definition("A_OK", "deger");
        var bad = Definition("B_BAD", "brut / (deger - deger)");

        var evaluation = PayItemEvaluator.Evaluate([(Assign(bad, 5m), bad), (Assign(ok, 5m), ok)], deger => Vars(Input(), value: deger));

        evaluation.Succeeded.ShouldBeFalse();
        evaluation.FailedCode.ShouldBe("B_BAD");
    }

    [Fact]
    public void Evaluate_ValueUsedButMissing_Fails()
    {
        var d = Definition("NOVALUE", "deger");

        PayItemEvaluator.Evaluate([(Assign(d, 1m), d)], _ => Vars(Input(), value: null)).FailedCode.ShouldBe("NOVALUE");
    }

    [Fact]
    public void Evaluate_UsesTheDefinitionDefault_WhenTheAssignmentHasNoValue()
    {
        var d = PayItemDefinition.Create(TenantId, "DEF", PayItemDomainTests.Data("deger", defaultValue: 400m)).Value;
        decimal? seen = -1m;

        var evaluation = PayItemEvaluator.Evaluate([(Assign(d, null), d)], deger =>
        {
            seen = deger;
            return Vars(Input(), value: deger);
        });

        seen.ShouldBe(400m);
        evaluation.Items.Single().Amount.ShouldBe(400m);
    }

    [Fact]
    public void Evaluate_NetItem_IsMarkedNet()
    {
        var d = Definition("NETPRIM", "deger", PayItemAmountBasis.Net);

        PayItemEvaluator.Evaluate([(Assign(d, 1_000m), d)], deger => Vars(Input(), value: deger)).Items.Single().IsNet.ShouldBeTrue();
    }

    [Fact]
    public void Allocate_SplitsByNetAmountsAndGivesTheKurusDifferenceToTheLastNetLine()
    {
        var (adjustment, items) = PayItemNetAllocator.Allocate(100.00m, 10m, [10m, 10m]);

        adjustment.ShouldBe(33.33m);
        items.ShouldBe([33.33m, 33.34m]);
        (adjustment + items.Sum()).ShouldBe(100.00m);
    }

    [Fact]
    public void Allocate_WithoutNetAdjustments_AllGoesToTheItemsAndTheSumIsExact()
    {
        var (adjustment, items) = PayItemNetAllocator.Allocate(1_234.57m, 0m, [300m, 700m, 1m]);

        adjustment.ShouldBe(0m);
        items.Sum().ShouldBe(1_234.57m);
        items.Count.ShouldBe(3);
    }

    [Fact]
    public void Allocate_ZeroNetLastItem_DoesNotReceiveTheRemainder()
    {
        var (_, items) = PayItemNetAllocator.Allocate(50.01m, 5m, [10m, 0m]);

        items[1].ShouldBe(0m);
        items[0].ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void NetAdjustmentPlusNetItem_SolvedTogether_LeaveTheEmployeeBothNetsWithinOneKurus()
    {
        var plain = Input(60_000m);
        var baselineNet = PayrollCalculator.Calculate(plain, Snapshot).NetAmount;
        const decimal netAdjustment = 5_000m;
        const decimal netItem = 10_000m;

        var solved = NetToGrossSolver.SolveExtra(netAdjustment + netItem, extra => PayrollCalculator.Calculate(plain with { AdditionalTaxableEarnings = extra }, Snapshot));
        var (adjustmentGross, itemGross) = PayItemNetAllocator.Allocate(solved, netAdjustment, [netItem]);
        var item = new PayItemEarning("NETPRIM", "Net prim", itemGross[0], true, true, true, "deger", new Dictionary<string, decimal> { ["deger"] = netItem }, netItem);
        var result = PayrollCalculator.Calculate(plain with { AdditionalTaxableEarnings = adjustmentGross, PayItems = [item] }, Snapshot);

        (adjustmentGross + itemGross[0]).ShouldBe(solved);
        Math.Abs(result.NetAmount - baselineNet - netAdjustment - netItem).ShouldBeLessThanOrEqualTo(0.01m);
        (adjustmentGross / solved).ShouldBe(1m / 3m, 0.001m); // brüt farkı net tutarlara oranla dağıtılır (5.000 : 10.000)
    }
}
