using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>The MUHSGK / SGK premium data set: read from a payslip's calculation steps, totalled, written as a semicolon CSV.</summary>
public sealed class DeclarationSummaryTests
{
    private static TraceStep Step(string code, decimal output) => new(1, code, "t", new Dictionary<string, string>(), "f", output, []);

    [Fact]
    public void FromSteps_ReadsThePremiumsAndNetsTheTaxesOfTheirMinimumWageExemptions()
    {
        var steps = new[]
        {
            Step(PayrollTraceStepCodes.SgkEmployee, 7_000m),
            Step(PayrollTraceStepCodes.UnempEmployee, 500m),
            Step(PayrollTraceStepCodes.IncomeTax, 6_000m),
            Step(PayrollTraceStepCodes.MinWageExemptionGv, 2_500m),
            Step(PayrollTraceStepCodes.StampTax, 380m),
            Step(PayrollTraceStepCodes.MinWageExemptionDv, 130m),
            Step(PayrollTraceStepCodes.EmployerSgk, 10_375m),
            Step(PayrollTraceStepCodes.EmployerUnemp, 1_000m),
        };

        var line = ExportDeclarationSummaryHandler.FromSteps("Ayşe Yılmaz", 50_000m, 38_000m, steps);

        line.SgkEmployee.ShouldBe(7_000m);
        line.UnemploymentEmployee.ShouldBe(500m);
        line.IncomeTax.ShouldBe(3_500m); // 6,000 - 2,500
        line.StampTax.ShouldBe(250m); // 380 - 130
        line.EmployerSgk.ShouldBe(10_375m);
        line.EmployerUnemployment.ShouldBe(1_000m);
        line.Gross.ShouldBe(50_000m);
        line.Net.ShouldBe(38_000m);
    }

    [Fact]
    public void FromSteps_TreatsAMissingTraceAsZeros_WithoutDroppingTheEmployee()
    {
        var line = ExportDeclarationSummaryHandler.FromSteps("Baran Demir", 30_000m, 24_000m, []);

        line.IncomeTax.ShouldBe(0m);
        line.SgkEmployee.ShouldBe(0m);
        line.Gross.ShouldBe(30_000m);
    }

    [Fact]
    public void Build_WritesTheHeader_TheLines_AndATotalRow()
    {
        var lines = new[]
        {
            new DeclarationLine("Ayşe Yılmaz", 50_000m, 7_000m, 500m, 3_500m, 250m, 38_000m, 10_375m, 1_000m),
            new DeclarationLine("Baran Demir", 30_000m, 4_200m, 300m, 1_000m, 100m, 24_000m, 6_225m, 600m),
        };

        var csv = DeclarationSummaryBuilder.Build(lines).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        csv[0].ShouldBe(DeclarationSummaryBuilder.Header);
        csv[1].ShouldBe("1;\"Ayşe Yılmaz\";50000.00;7000.00;500.00;3500.00;250.00;38000.00;10375.00;1000.00");
        csv[3].ShouldBe(";\"TOPLAM\";80000.00;11200.00;800.00;4500.00;350.00;62000.00;16600.00;1600.00");
    }
}
