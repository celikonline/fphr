using System.Text.Json;
using NSubstitute;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Application.Declarations;
using Sense.Modules.Payroll.Application.PayItems;
using Sense.Modules.Payroll.Domain;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Modules.Payroll.Domain.Payslips;
using Sense.Modules.Payroll.Domain.Periods;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Export;
using Shouldly;
using Xunit;

namespace Sense.Modules.Payroll.Tests.PayItems;

/// <summary>PAY-DEDUCTION-1: the payslip ("Kesintiler" rows) and the pay-item-lines endpoint (`direction`) for formula deductions.</summary>
public sealed class PayDeductionDocumentTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static TraceStep Plain(string code, decimal output, IReadOnlyDictionary<string, string>? inputs = null) =>
        new(0, code, "title", inputs ?? new Dictionary<string, string>(), "formula", output, []);

    private static TraceStep ItemStep(string code, string name, decimal amount, bool deduction)
    {
        var inputs = new Dictionary<string, string> { ["code"] = code, ["name"] = name };
        if (deduction)
        {
            inputs["direction"] = "deduction";
            inputs["deductionAmount"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new TraceStep(0, PayrollTraceStepCodes.PayItem, "title", inputs, "brut * 0.01", amount, [], code);
    }

    /// <summary>brüt 51,500 (50,000 maktu + 1,500 yemek kalemi); DEDUCTIONS = 1,000 icra/avans/sendika + 800 + 200 formüllü = 2,000.</summary>
    private static List<TraceStep> Steps() =>
    [
        Plain(PayrollTraceStepCodes.Days, 30, new Dictionary<string, string> { ["workedDays"] = "30", ["calendarDaysInMonth"] = "30" }),
        ItemStep("YEMEK", "Yemek yardımı", 1_500m, deduction: false),
        ItemStep("A_ESKI", "Kooperatif aidatı", 800m, deduction: true),
        ItemStep("B_ESKI", "Kefalet kesintisi", 200m, deduction: true),
        Plain(PayrollTraceStepCodes.Deductions, 2_000m),
    ];

    private static IEnumerable<string> PdfRows(SimplePdfDocument document, string heading) =>
        document.Blocks.OfType<SimplePdfTable>().Single(t => t.Heading == heading).Rows.Select(r => string.Join(" | ", r));

    [Fact]
    public void Text_ListsEachDeductionOnItsOwnRow_AndKeepsDeductionsOutOfTheEarnings()
    {
        var text = PayslipDocumentBuilder.BuildText("Ayşe Yılmaz", 2026, 9, Steps(), 51_500m, 40_000m);

        var earnings = text[..text.IndexOf("KESİNTİLER", StringComparison.Ordinal)];
        earnings.ShouldContain("Yemek yardımı: 1.500,00 TL");
        earnings.ShouldContain("Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti): 50.000,00 TL");
        earnings.ShouldContain("Toplam Brüt: 51.500,00 TL");
        earnings.ShouldNotContain("Kooperatif");
        var deductions = text[text.IndexOf("KESİNTİLER", StringComparison.Ordinal)..];
        deductions.ShouldContain("Kooperatif aidatı: 800,00 TL");
        deductions.ShouldContain("Kefalet kesintisi: 200,00 TL");
        deductions.ShouldContain("İcra/Nafaka/Avans Kesintileri: 1.000,00 TL"); // 2,000 - 800 - 200: the formula rows are not counted twice
    }

    [Fact]
    public void Pdf_HasTheSameDeductionRows()
    {
        var document = PayslipDocumentBuilder.BuildPdf("Ayşe Yılmaz", null, 2026, 9, Steps(), 51_500m, 40_000m);

        var rows = PdfRows(document, "Kesintiler").ToList();
        rows.ShouldContain("İcra/Nafaka/Avans Kesintileri | 1.000,00");
        rows.ShouldContain("Kooperatif aidatı | 800,00");
        rows.ShouldContain("Kefalet kesintisi | 200,00");
        rows.Count.ShouldBe(7);
        PdfRows(document, "Kazançlar").ShouldNotContain(r => r.Contains("Kooperatif"));
    }

    [Fact]
    public void OnlyDeductionItems_DoNotTurnTheEarningsIntoAnItemisedTable()
    {
        // No earning pay item: the single gross row of old payslips stays, the deduction only adds Kesintiler rows.
        List<TraceStep> steps = [ItemStep("A_ESKI", "Kooperatif aidatı", 800m, deduction: true), Plain(PayrollTraceStepCodes.Deductions, 800m)];

        var text = PayslipDocumentBuilder.BuildText("Ayşe Yılmaz", 2026, 9, steps, 50_000m, 40_000m);

        text.ShouldNotContain("Toplam Brüt");
        text.ShouldContain("KAZANÇLAR\n  Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti): 50.000,00 TL\n\nKESİNTİLER");
        text.ShouldContain("Kooperatif aidatı: 800,00 TL");
        text.ShouldContain("İcra/Nafaka/Avans Kesintileri: 0,00 TL");
    }

    [Fact]
    public void WithoutDeductionItems_TheDeductionRowsAreTheOldFive()
    {
        List<TraceStep> steps = [Plain(PayrollTraceStepCodes.Deductions, 300m)];

        var document = PayslipDocumentBuilder.BuildPdf("Ayşe Yılmaz", null, 2026, 9, steps, 50_000m, 40_000m);

        PdfRows(document, "Kesintiler").Count().ShouldBe(5);
        PdfRows(document, "Kesintiler").Last().ShouldBe("İcra/Nafaka/Avans Kesintileri | 300,00");
    }

    private static (ListPeriodPayItemLinesHandler Handler, PayrollPeriod Period, Guid EmployeeId) ArrangeLines()
    {
        var period = PayrollPeriod.Create(TenantId, 2026, 9).Value;
        var employeeId = Guid.NewGuid();
        var slip = Payslip.CreateFirstVersion(TenantId, employeeId, period.Id, "{}", "h", 100m, 80m, 120m);
        var periods = Substitute.For<IPayrollPeriodRepository>();
        periods.FindByIdAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<PayrollPeriod?>(period));
        var payslips = Substitute.For<IPayslipRepository>();
        payslips.ListByPeriodAsync(period.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Payslip>>([slip]));
        var traces = Substitute.For<IPayslipCalculationTraceRepository>();
        traces.FindLatestByPayslipIdAsync(slip.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PayslipCalculationTrace?>(PayslipCalculationTrace.Create(
                TenantId, slip.Id, Guid.NewGuid(), 1, JsonSerializer.Serialize(Steps(), Json), Guid.NewGuid(), DateTime.UtcNow)));
        var directory = Substitute.For<IEmployeeDirectory>();
        directory.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(employeeId, "Ada", "Yılmaz", null, null, null, "Active")]));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return (new ListPeriodPayItemLinesHandler(periods, payslips, traces, directory, tenant), period, employeeId);
    }

    [Fact]
    public async Task Lines_CarryTheDirection_EarningByDefault_DeductionFromTheTrace()
    {
        var (handler, period, employeeId) = ArrangeLines();

        var result = await handler.Handle(new ListPeriodPayItemLinesQuery(period.Id), CancellationToken.None);

        result.Value.ShouldBe(
        [
            new PayItemLineDto(employeeId, "Ada Yılmaz", "YEMEK", "Yemek yardımı", 1_500m, "gross", null, "earning"),
            new PayItemLineDto(employeeId, "Ada Yılmaz", "A_ESKI", "Kooperatif aidatı", 800m, "gross", null, "deduction"),
            new PayItemLineDto(employeeId, "Ada Yılmaz", "B_ESKI", "Kefalet kesintisi", 200m, "gross", null, "deduction"),
        ]);
        result.Value.ShouldAllBe(l => l.Direction == "earning" || l.Direction == "deduction");
    }

    [Fact]
    public async Task Lines_SerializeTheDirectionAlways_NetTargetStillOmitted()
    {
        var (handler, period, _) = ArrangeLines();
        var result = await handler.Handle(new ListPeriodPayItemLinesQuery(period.Id), CancellationToken.None);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var earning = JsonSerializer.Serialize(result.Value[0], options);
        var deduction = JsonSerializer.Serialize(result.Value[1], options);

        earning.ShouldContain("\"direction\":\"earning\"");
        deduction.ShouldContain("\"direction\":\"deduction\"");
        deduction.ShouldNotContain("netTarget");
    }
}
