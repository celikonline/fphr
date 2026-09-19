using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Domain.Calculation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests;

/// <summary>Pure unit tests for <see cref="PayslipDocumentBuilder"/> (senseik-019 3a, ücret pusulası - İş K. md.37).</summary>
public sealed class PayslipDocumentBuilderTests
{
    private static TraceStep Step(string code, decimal output, IReadOnlyDictionary<string, string>? inputs = null) =>
        new(0, code, "title", inputs ?? new Dictionary<string, string>(), "formula", output, []);

    [Fact]
    public void BuildText_IncludesEmployeeNamePeriodAndNetAmount()
    {
        var steps = new List<TraceStep>
        {
            Step(PayrollTraceStepCodes.Days, 30, new Dictionary<string, string> { ["workedDays"] = "30", ["calendarDaysInMonth"] = "30" }),
            Step(PayrollTraceStepCodes.SgkEmployee, 28_000m),
            Step(PayrollTraceStepCodes.UnempEmployee, 2_000m),
            Step(PayrollTraceStepCodes.IncomeTax, 26_100m),
            Step(PayrollTraceStepCodes.MinWageExemptionGv, 3_825m),
            Step(PayrollTraceStepCodes.StampTax, 1_518m),
            Step(PayrollTraceStepCodes.MinWageExemptionDv, 227.70m),
            Step(PayrollTraceStepCodes.Deductions, 0m),
        };

        var text = PayslipDocumentBuilder.BuildText("Ayşe Yılmaz", 2026, 1, steps, 200_000m, 146_434.70m);

        text.ShouldContain("Ayşe Yılmaz");
        text.ShouldContain("2026-01");
        text.ShouldContain("Çalışılan gün: 30 / 30");
        text.ShouldContain("NET ÖDENEN: 146.434,70 TL");
    }

    [Fact]
    public void BuildText_ShowsIncomeTaxAfterExemption_NotBeforeExemption()
    {
        var steps = new List<TraceStep>
        {
            Step(PayrollTraceStepCodes.IncomeTax, 26_100m),
            Step(PayrollTraceStepCodes.MinWageExemptionGv, 3_825m),
        };

        var text = PayslipDocumentBuilder.BuildText("Test Çalışan", 2026, 1, steps, 200_000m, 100_000m);

        // 26,100 - 3,825 = 22,275 (istisna sonrası gelir vergisi), 26.100 değil.
        text.ShouldContain("22.275,00 TL");
        text.ShouldNotContain("26.100,00 TL");
    }

    [Fact]
    public void BuildText_MissingSteps_DefaultsToZeroRatherThanThrowing()
    {
        var text = PayslipDocumentBuilder.BuildText("Test Çalışan", 2026, 1, [], 50_000m, 50_000m);

        text.ShouldContain("NET ÖDENEN: 50.000,00 TL");
    }
}
