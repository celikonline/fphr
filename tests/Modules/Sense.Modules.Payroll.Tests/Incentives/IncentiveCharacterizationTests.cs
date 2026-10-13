using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Profiles;
using Sense.Modules.Employee.Contracts;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.Incentives;

/// <summary>
/// PAY-INCENTIVE-3 characterization: the calculation of an employee WITHOUT incentive models must stay byte-identical. These
/// fixtures (amounts + the whole <c>result.Steps</c> JSON) were recorded against the engine BEFORE the incentive integration and
/// are replayed unchanged afterwards; each scenario runs with <c>IncentiveEligible</c> true (legacy 5-point profile rule) and false.
/// Calculator-level scenarios use the real 2026 legislation values (asgari ücret 33.030); the net-wage scenario runs through the service.
/// </summary>
public sealed class IncentiveCharacterizationTests
{
    private static readonly PayrollLegislationSnapshot Snapshot = IncentiveTestKit.Snapshot;

    public static TheoryData<string, bool> Scenarios()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in new[] { "monthly_full", "prorated_15_days", "daily_wage", "hourly_wage", "pay_items", "disabled_degree_2" })
        {
            data.Add(name, true);
            data.Add(name, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void TheTraceAndAmounts_OfAnEmployeeWithoutIncentiveModels_AreUnchanged(string scenario, bool incentiveEligible)
    {
        var input = Input(scenario) with { IncentiveEligible = incentiveEligible };

        var result = PayrollCalculator.Calculate(input, Snapshot);

        IncentiveTestKit.MatchesFixture($"{scenario}.{(incentiveEligible ? "eligible" : "plain")}", IncentiveTestKit.Describe(result));
    }

    [Fact]
    public void TheRecordedScenarios_AreNotVacuous_TheLegacyRuleStillChangesOnlyTheEmployerDiscount()
    {
        // Sanity of the fixtures themselves: eligible vs plain differ in EMPLOYER_DISCOUNT and the employer cost only, never in the net.
        foreach (var scenario in new[] { "monthly_full", "prorated_15_days", "daily_wage", "hourly_wage", "pay_items", "disabled_degree_2" })
        {
            var plain = PayrollCalculator.Calculate(Input(scenario), Snapshot);
            var eligible = PayrollCalculator.Calculate(Input(scenario) with { IncentiveEligible = true }, Snapshot);

            plain.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBe(0m, scenario);
            eligible.Step(PayrollTraceStepCodes.EmployerDiscount).Output.ShouldBeGreaterThan(0m, scenario);
            eligible.NetAmount.ShouldBe(plain.NetAmount, scenario);
            eligible.EmployerCost.ShouldBeLessThan(plain.EmployerCost, scenario);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ANetWageEmployee_ThroughTheService_KeepsTheRecordedTraceAndTheNetTarget(bool profileHasLawCode)
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee(profileHasLawCode ? "05510" : null, salary: 40_000m, type: EmployeeSalaryType.Net);

        var summary = await harness.RunAsync(employee);

        summary.CalculatedCount.ShouldBe(1);
        IncentiveTestKit.MatchesFixture($"net_wage.{(profileHasLawCode ? "eligible" : "plain")}", harness.DescribeStored(employee));
        harness.PayslipOf(employee).NetAmount.ShouldBe(40_000m, 0.01m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMonthlyEmployee_ThroughTheService_KeepsTheRecordedTrace(bool profileHasLawCode)
    {
        var harness = IncentiveServiceHarness.Create();
        var employee = harness.AddEmployee(profileHasLawCode ? "05510" : null, salary: 50_000m);

        await harness.RunAsync(employee);

        IncentiveTestKit.MatchesFixture($"service_monthly.{(profileHasLawCode ? "eligible" : "plain")}", harness.DescribeStored(employee));
    }

    private static PayrollCalculationInput Input(string scenario) => scenario switch
    {
        // 200.000 brüt, tam ay (dilim sınırı 190.000'i aşar: iki dilimli vergi).
        "monthly_full" => IncentiveTestKit.Plain(200_000m),

        // 15 gün kıst: ücret, SGK taban/tavan ve asgari ücret istisnaları 15/30 oranlanır.
        "prorated_15_days" => new PayrollCalculationInput(50_000m, 15, 30, MealBenefitVariant.None, 0m, 0m, IncentiveTestKit.AsOf, ProratedPaidDays: 15),

        // Günlük ücretli: 2.000 TL x 22 ücretli gün = 44.000; aylık karşılık 60.000.
        "daily_wage" => new PayrollCalculationInput(60_000m, 22, 30, MealBenefitVariant.None, 0m, 0m, IncentiveTestKit.AsOf,
            ProratedPaidDays: 22, Wage: new PeriodWage(WageBasis.Daily, 2_000m, 22)),

        // Saatlik ücretli: 250 TL x 7,5 x 20 ücretli gün = 37.500; aylık karşılık 56.250.
        "hourly_wage" => new PayrollCalculationInput(56_250m, 20, 30, MealBenefitVariant.None, 0m, 0m, IncentiveTestKit.AsOf,
            ProratedPaidDays: 20, Wage: new PeriodWage(WageBasis.Hourly, 250m, 20)),

        // Ödeme kalemli bordro: tam vergiye tabi prim + SGK ve damgadan muaf yardım.
        "pay_items" => IncentiveTestKit.Plain(80_000m) with
        {
            PayItems =
            [
                new PayItemEarning("PRIM", "Prim", 10_000m, true, true, true, "deger", new Dictionary<string, decimal> { ["deger"] = 10_000m }),
                new PayItemEarning("YARDIM", "Yardım", 3_000m, false, true, false, "3000", new Dictionary<string, decimal>()),
            ],
        },

        // Engelli 2. derece: GV matrahından 4.000 TL indirim.
        "disabled_degree_2" => IncentiveTestKit.Plain(200_000m) with { DisabilityDegree = 2 },

        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };
}
