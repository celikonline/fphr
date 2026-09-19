using System.Globalization;
using System.Text;
using Sense.Modules.Payroll.Domain.Calculation;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>
/// Ücret pusulası (senseik-019 3a, 4857 s. İş Kanunu md.37): işveren, ücretin ödendiği her seferinde çalışana ücret
/// hesabını gösterir bir pusula vermek zorundadır - ücretin unsurları, varsa fazla mesai/prim gibi ek kalemler ve
/// yapılan bütün kesintiler ile sebepleri gösterilmelidir. Bu sürüm yalnızca DÜZ METİN çıktı üretir; şık bir PDF
/// baskısı Shared'ın <c>IDocumentRenderer</c> (QuestPDF) implementasyonunu bekliyor - bkz. handoff "Kalan İşler".
/// </summary>
public static class PayslipDocumentBuilder
{
    public static string BuildText(string employeeName, int year, int month, IReadOnlyList<TraceStep> steps, decimal grossAmount, decimal netAmount)
    {
        var sb = new StringBuilder();
        sb.Append("ÜCRET PUSULASI (4857 s. İş Kanunu md.37)\n");
        sb.Append("Çalışan: ").Append(employeeName).Append('\n');
        sb.Append("Dönem: ").Append(year.ToString("D4", CultureInfo.InvariantCulture)).Append('-').Append(month.ToString("D2", CultureInfo.InvariantCulture)).Append('\n');

        var days = Find(steps, PayrollTraceStepCodes.Days);
        if (days is not null)
        {
            sb.Append("Çalışılan gün: ").Append(days.Inputs.GetValueOrDefault("workedDays")).Append(" / ")
                .Append(days.Inputs.GetValueOrDefault("calendarDaysInMonth")).Append('\n');
        }

        sb.Append("\nKAZANÇLAR\n");
        AppendLine(sb, "Brüt Ücret (maktu + varsa fazla mesai/tatil ücreti)", grossAmount);

        sb.Append("\nKESİNTİLER\n");
        AppendStep(sb, steps, "SGK İşçi Payı", PayrollTraceStepCodes.SgkEmployee);
        AppendStep(sb, steps, "İşsizlik Sigortası İşçi Payı", PayrollTraceStepCodes.UnempEmployee);
        AppendAfterExemption(sb, steps, "Gelir Vergisi (asgari ücret istisnası sonrası)", PayrollTraceStepCodes.IncomeTax, PayrollTraceStepCodes.MinWageExemptionGv);
        AppendAfterExemption(sb, steps, "Damga Vergisi (asgari ücret istisnası sonrası)", PayrollTraceStepCodes.StampTax, PayrollTraceStepCodes.MinWageExemptionDv);
        AppendStep(sb, steps, "İcra/Nafaka/Avans Kesintileri", PayrollTraceStepCodes.Deductions);

        sb.Append("\nNET ÖDENEN: ").Append(Format(netAmount)).Append(" TL\n");

        return sb.ToString();
    }

    private static void AppendStep(StringBuilder sb, IReadOnlyList<TraceStep> steps, string label, string code)
    {
        var value = Find(steps, code)?.Output ?? 0m;
        AppendLine(sb, label, value);
    }

    /// <summary>Gelir/damga vergisi trace adımlarının Output'u istisna TUTARIdır, ödenen tutar değil - bu yüzden
    /// vergi - istisna farkını gösterir (ExportPayrollPeriodHandler.ExtractFigures ile aynı yorum).</summary>
    private static void AppendAfterExemption(StringBuilder sb, IReadOnlyList<TraceStep> steps, string label, string taxCode, string exemptionCode)
    {
        var tax = Find(steps, taxCode)?.Output ?? 0m;
        var exemption = Find(steps, exemptionCode)?.Output ?? 0m;
        AppendLine(sb, label, Math.Max(0m, tax - exemption));
    }

    private static void AppendLine(StringBuilder sb, string label, decimal amount) =>
        sb.Append("  ").Append(label).Append(": ").Append(Format(amount)).Append(" TL\n");

    private static TraceStep? Find(IReadOnlyList<TraceStep> steps, string code) => steps.FirstOrDefault(s => s.Code == code);

    private static string Format(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));
}
