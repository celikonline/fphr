using System.Globalization;
using System.Text;
using Sense.Modules.Payroll.Domain.Calculation;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>
/// Ücret pusulası (senseik-019 3a, 4857 s. İş Kanunu md.37): işveren, ücretin ödendiği her seferinde çalışana ücret
/// hesabını gösterir bir pusula vermek zorundadır - ücretin unsurları, varsa fazla mesai/prim gibi ek kalemler ve
/// yapılan bütün kesintiler ile sebepleri gösterilmelidir. Düz metin (<see cref="BuildText"/>) ve PDF belge modeli
/// (<see cref="BuildPdf"/>, Shared'ın <see cref="ISimplePdfWriter"/>'ı ile basılır) aynı kalemleri gösterir.
/// </summary>
public static class PayslipDocumentBuilder
{
    private const string Title = "ÜCRET PUSULASI (4857 s. İş Kanunu md.37)";
    private const string GrossLabel = "Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti)";
    private const string NetLabel = "NET ÖDENEN";

    public static string BuildText(string employeeName, int year, int month, IReadOnlyList<TraceStep> steps, decimal grossAmount, decimal netAmount)
    {
        var sb = new StringBuilder();
        sb.Append(Title).Append('\n');
        sb.Append("Çalışan: ").Append(employeeName).Append('\n');
        sb.Append("Dönem: ").Append(PeriodLabel(year, month)).Append('\n');

        if (WorkedDays(steps) is { } days)
        {
            sb.Append("Çalışılan gün: ").Append(days).Append('\n');
        }

        sb.Append("\nKAZANÇLAR\n");
        AppendLine(sb, GrossLabel, grossAmount);

        sb.Append("\nKESİNTİLER\n");
        foreach (var (label, amount) in Deductions(steps))
        {
            AppendLine(sb, label, amount);
        }

        sb.Append('\n').Append(NetLabel).Append(": ").Append(Format(netAmount)).Append(" TL\n");

        return sb.ToString();
    }

    /// <summary>The same content as <see cref="BuildText"/> as a PDF document model; <paramref name="issuer"/> is the employer name, if known.</summary>
    public static SimplePdfDocument BuildPdf(
        string employeeName, string? issuer, int year, int month, IReadOnlyList<TraceStep> steps, decimal grossAmount, decimal netAmount)
    {
        var identity = new List<KeyValuePair<string, string>>
        {
            new("Çalışan", employeeName),
            new("Dönem", PeriodLabel(year, month)),
        };
        if (WorkedDays(steps) is { } days)
        {
            identity.Add(new("Çalışılan gün", days));
        }

        string[] columns = ["Kalem", "Tutar (TL)"];
        int[] amountColumn = [1];
        return new SimplePdfDocument(
            Title,
            issuer,
            [
                new SimplePdfKeyValues(null, identity),
                new SimplePdfTable("Kazançlar", columns, [[GrossLabel, Format(grossAmount)]], amountColumn),
                new SimplePdfTable("Kesintiler", columns, Deductions(steps).Select(d => (IReadOnlyList<string>)[d.Label, Format(d.Amount)]).ToList(), amountColumn),
                new SimplePdfParagraph($"{NetLabel}: {Format(netAmount)} TL", Bold: true),
            ],
            "Bu pusula SenseİK bordro kayıtlarından oluşturulmuştur.");
    }

    /// <summary>
    /// Every deduction with its reason. Gelir/damga vergisi trace adımlarının Output'u istisna TUTARIdır, ödenen tutar
    /// değil - bu yüzden vergi - istisna farkını gösterir (ExportPayrollPeriodHandler.ExtractFigures ile aynı yorum).
    /// </summary>
    private static IReadOnlyList<(string Label, decimal Amount)> Deductions(IReadOnlyList<TraceStep> steps) =>
    [
        ("SGK İşçi Payı", Output(steps, PayrollTraceStepCodes.SgkEmployee)),
        ("İşsizlik Sigortası İşçi Payı", Output(steps, PayrollTraceStepCodes.UnempEmployee)),
        ("Gelir Vergisi (asgari ücret istisnası sonrası)", AfterExemption(steps, PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.MinWageExemptionGv)),
        ("Damga Vergisi (asgari ücret istisnası sonrası)", AfterExemption(steps, PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.MinWageExemptionDv)),
        ("İcra/Nafaka/Avans Kesintileri", Output(steps, PayrollTraceStepCodes.Deductions)),
    ];

    private static string? WorkedDays(IReadOnlyList<TraceStep> steps)
    {
        var days = Find(steps, PayrollTraceStepCodes.Days);
        return days is null
            ? null
            : $"{days.Inputs.GetValueOrDefault("workedDays")} / {days.Inputs.GetValueOrDefault("calendarDaysInMonth")}";
    }

    private static string PeriodLabel(int year, int month) =>
        $"{year.ToString("D4", CultureInfo.InvariantCulture)}-{month.ToString("D2", CultureInfo.InvariantCulture)}";

    private static decimal Output(IReadOnlyList<TraceStep> steps, string code) => Find(steps, code)?.Output ?? 0m;

    private static decimal AfterExemption(IReadOnlyList<TraceStep> steps, string taxCode, string exemptionCode) =>
        Math.Max(0m, Output(steps, taxCode) - Output(steps, exemptionCode));

    private static void AppendLine(StringBuilder sb, string label, decimal amount) =>
        sb.Append("  ").Append(label).Append(": ").Append(Format(amount)).Append(" TL\n");

    private static TraceStep? Find(IReadOnlyList<TraceStep> steps, string code) => steps.FirstOrDefault(s => s.Code == code);

    private static string Format(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
