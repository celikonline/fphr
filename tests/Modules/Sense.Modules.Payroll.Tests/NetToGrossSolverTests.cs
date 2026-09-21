using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Net→brüt: the gross the payroll must run on so the employee keeps a fixed net, checked by running the real calculator back.</summary>
public sealed class NetToGrossSolverTests
{
    private static readonly PayrollLegislationSnapshot Legislation = new(
        [new TaxBracket(100_000m, 0.15m), new TaxBracket(200_000m, 0.20m), new TaxBracket(500_000m, 0.27m), new TaxBracket(null, 0.35m)],
        SgkFloor: 0m, SgkCeiling: 300_000m, SgkEmployeeRate: 0.14m, UnemploymentEmployeeRate: 0.01m,
        SgkEmployerRate: 0.205m, UnemploymentEmployerRate: 0.02m, SgkEmployerDiscountRate: 0m, StampTaxRate: 0.00759m,
        MinimumWageGross: 20_000m, MealIncomeTaxDailyExemption: 0m, MealSgkDailyExemptionRate: 0m, MealMarketCardExemptionApplies: false,
        DisabilityMonthlyTaxReductionDegree1: 0m, DisabilityMonthlyTaxReductionDegree2: 0m, DisabilityMonthlyTaxReductionDegree3: 0m,
        AsOf: new DateOnly(2026, 8, 1));

    private static PayrollCalculationResult Run(decimal gross, decimal priorCumulativeBase) =>
        PayrollCalculator.Calculate(
            new PayrollCalculationInput(
                GrossMonthlySalary: gross, WorkedDays: 30, CalendarDaysInMonth: 30, MealVariant: MealBenefitVariant.None, MealDailyAmount: 0m,
                PriorCumulativeGvBase: priorCumulativeBase, AsOfDate: new DateOnly(2026, 8, 1)),
            Legislation);

    [Theory]
    [InlineData(25_000, 0)]
    [InlineData(60_000, 0)]
    [InlineData(90_000, 350_000)] // crosses into a higher bracket mid-year
    public void Solve_FindsAGross_WhoseCalculationLeavesTheTargetNet(int target, int priorBase)
    {
        var gross = NetToGrossSolver.Solve(target, g => Run(g, priorBase));

        gross.ShouldBeGreaterThan(target);
        var net = NetToGrossSolver.SalaryNet(Run(gross, priorBase));
        Math.Abs(net - target).ShouldBeLessThan(0.05m);
    }

    [Fact]
    public void Solve_NeedsAHigherGross_WhenTheCumulativeBaseAlreadySitsInAHigherBracket()
    {
        var early = NetToGrossSolver.Solve(60_000m, g => Run(g, 0m));
        var late = NetToGrossSolver.Solve(60_000m, g => Run(g, 450_000m));

        late.ShouldBeGreaterThan(early);
    }

    [Fact]
    public void Solve_RejectsANonPositiveNet()
    {
        Should.Throw<Exception>(() => NetToGrossSolver.Solve(0m, g => Run(g, 0m)));
    }
}
