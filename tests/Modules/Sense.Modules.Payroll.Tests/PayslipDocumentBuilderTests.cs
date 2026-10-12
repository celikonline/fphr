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

    // ---- PAY-FORMULA-3: kalemli pusula; kalemsiz çıktı aynı ----

    private static TraceStep PayItemStep(string code, string name, decimal amount, decimal? netTarget = null)
    {
        var inputs = new Dictionary<string, string> { ["code"] = code, ["name"] = name };
        if (netTarget is { } net)
        {
            inputs["netTarget"] = net.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new TraceStep(0, PayrollTraceStepCodes.PayItem, "title", inputs, "deger", amount, [], code);
    }

    private static List<TraceStep> StepsWithItems() =>
    [
        Step(PayrollTraceStepCodes.Days, 30, new Dictionary<string, string> { ["workedDays"] = "30", ["calendarDaysInMonth"] = "30" }),
        PayItemStep("YEMEK", "Yemek yardımı", 1_500m),
        PayItemStep("NETPRIM", "Net prim", 12_345.67m, 10_000m),
    ];

    private static IEnumerable<string> PdfRows(Sense.Shared.Contracts.Export.SimplePdfDocument document, string tableTitle) =>
        document.Blocks.OfType<Sense.Shared.Contracts.Export.SimplePdfTable>().Single(t => t.Heading == tableTitle).Rows
            .Select(r => string.Join(" | ", r));

    [Fact]
    public void BuildText_WithPayItems_ListsEachItemAndTheTotalGross()
    {
        var text = PayslipDocumentBuilder.BuildText("Ayşe Yılmaz", 2026, 9, StepsWithItems(), 63_845.67m, 50_000m);

        text.ShouldContain("Yemek yardımı: 1.500,00 TL");
        text.ShouldContain("Net prim (net 10.000,00 TL): 12.345,67 TL");
        text.ShouldContain("Toplam Brüt: 63.845,67 TL");
        text.ShouldContain("Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti): 50.000,00 TL"); // 63.845,67 - 1.500 - 12.345,67
    }

    [Fact]
    public void BuildPdf_WithPayItems_HasTheSameEarningsRows()
    {
        var document = PayslipDocumentBuilder.BuildPdf("Ayşe Yılmaz", "Acme", 2026, 9, StepsWithItems(), 63_845.67m, 50_000m);

        PdfRows(document, "Kazançlar").ToList().ShouldBe(
        [
            "Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti) | 50.000,00",
            "Yemek yardımı | 1.500,00",
            "Net prim (net 10.000,00 TL) | 12.345,67",
            "Toplam Brüt | 63.845,67",
        ]);
    }

    [Fact]
    public void Build_WithoutPayItems_KeepsTheSingleGrossRow()
    {
        var steps = new List<TraceStep> { Step(PayrollTraceStepCodes.Days, 30, new Dictionary<string, string> { ["workedDays"] = "30", ["calendarDaysInMonth"] = "30" }) };

        var text = PayslipDocumentBuilder.BuildText("Ayşe Yılmaz", 2026, 9, steps, 50_000m, 40_000m);
        var document = PayslipDocumentBuilder.BuildPdf("Ayşe Yılmaz", null, 2026, 9, steps, 50_000m, 40_000m);

        text.ShouldContain("KAZANÇLAR\n  Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti): 50.000,00 TL\n\nKESİNTİLER");
        text.ShouldNotContain("Toplam Brüt");
        PdfRows(document, "Kazançlar").ToList().ShouldBe(["Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti) | 50.000,00"]);
    }
}
