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
    private const string TotalGrossLabel = "Toplam Brüt";

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
        foreach (var (label, amount) in Earnings(steps, grossAmount))
        {
            AppendLine(sb, label, amount);
        }

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
                new SimplePdfTable("Kazançlar", columns, Earnings(steps, grossAmount).Select(e => (IReadOnlyList<string>)[e.Label, Format(e.Amount)]).ToList(), amountColumn),
                new SimplePdfTable("Kesintiler", columns, Deductions(steps).Select(d => (IReadOnlyList<string>)[d.Label, Format(d.Amount)]).ToList(), amountColumn),
                new SimplePdfParagraph($"{NetLabel}: {Format(netAmount)} TL", Bold: true),
            ],
            "Bu pusula SenseİK bordro kayıtlarından oluşturulmuştur.");
    }

    /// <summary>
    /// The Kazançlar rows. Without pay items (no PAY_ITEM trace step) the single gross row of old payslips, unchanged. With pay items:
    /// the base gross row (total minus the items), one row per item (a net item shows its net target) and "Toplam Brüt".
    /// </summary>
    private static IReadOnlyList<(string Label, decimal Amount)> Earnings(IReadOnlyList<TraceStep> steps, decimal grossAmount)
    {
        var payItems = steps.Where(s => s.Code == PayrollTraceStepCodes.PayItem && !IsDeduction(s)).ToList();
        if (payItems.Count == 0)
        {
            return [(GrossLabel, grossAmount)];
        }

        var rows = new List<(string Label, decimal Amount)> { (GrossLabel, grossAmount - payItems.Sum(s => s.Output)) };
        foreach (var step in payItems)
        {
            var name = step.Inputs.GetValueOrDefault("name") ?? step.PayItem ?? string.Empty;
            var label = step.Inputs.TryGetValue("netTarget", out var raw) && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var net)
                ? $"{name} (net {Format(net)} TL)"
                : name;
            rows.Add((label, step.Output));
        }

        rows.Add((TotalGrossLabel, grossAmount));
        return rows;
    }

    /// <summary>
    /// Every deduction with its reason. Gelir/damga vergisi trace adımlarının Output'u istisna TUTARIdır, ödenen tutar
    /// değil - bu yüzden vergi - istisna farkını gösterir (ExportPayrollPeriodHandler.ExtractFigures ile aynı yorum).
    /// </summary>
    private static IReadOnlyList<(string Label, decimal Amount)> Deductions(IReadOnlyList<TraceStep> steps)
    {
        // Formüllü kesintiler (PAY-DEDUCTION) DEDUCTIONS toplamının içindedir; kendi satırlarına ayrılır, toplu satır geri kalanı gösterir.
        var payItemDeductions = steps.Where(s => s.Code == PayrollTraceStepCodes.PayItem && IsDeduction(s)).ToList();
        List<(string Label, decimal Amount)> rows =
        [
            ("SGK İşçi Payı", Output(steps, PayrollTraceStepCodes.SgkEmployee)),
            ("İşsizlik Sigortası İşçi Payı", Output(steps, PayrollTraceStepCodes.UnempEmployee)),
            ("Gelir Vergisi (asgari ücret istisnası sonrası)", AfterExemption(steps, PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.MinWageExemptionGv)),
            ("Damga Vergisi (asgari ücret istisnası sonrası)", AfterExemption(steps, PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.MinWageExemptionDv)),
            ("İcra/Nafaka/Avans Kesintileri", Output(steps, PayrollTraceStepCodes.Deductions) - payItemDeductions.Sum(s => s.Output)),
        ];
        rows.AddRange(payItemDeductions.Select(s => (s.Inputs.GetValueOrDefault("name") ?? s.PayItem ?? string.Empty, s.Output)));
        return rows;
    }

    private static bool IsDeduction(TraceStep step) => step.Inputs.GetValueOrDefault("direction") == "deduction";

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
