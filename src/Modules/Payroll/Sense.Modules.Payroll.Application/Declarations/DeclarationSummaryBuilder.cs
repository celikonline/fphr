using System.Text;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>One employee's figures for the monthly tax/SGK declarations, straight from the payslip's calculation steps.</summary>
public sealed record DeclarationLine(
    string EmployeeName,
    decimal Gross,
    decimal SgkEmployee,
    decimal UnemploymentEmployee,
    decimal IncomeTax,
    decimal StampTax,
    decimal Net,
    decimal EmployerSgk,
    decimal EmployerUnemployment);

/// <summary>
/// The data set behind the monthly Muhtasar ve Prim Hizmet Beyannamesi (MUHSGK) and the SGK premium payment: per employee and in total, the
/// gross, the employee's SGK and unemployment premiums, the income and stamp tax payable (after the minimum-wage exemptions) and the
/// employer's premiums. It is a reconciliation/preparation file - <b>not</b> the official e-Beyanname or e-Bildirge format - so the accountant
/// can check the declaration against the payroll. CSV, semicolon-separated, UTF-8 with a BOM like the bank list.
/// </summary>
public static class DeclarationSummaryBuilder
{
    public const string Header = "SiraNo;AdSoyad;Brut;SGKIsciPayi;IssizlikIsciPayi;GelirVergisi;DamgaVergisi;Net;SGKIsverenPayi;IssizlikIsverenPayi";

    public static string Build(IReadOnlyList<DeclarationLine> lines)
    {
        var text = new StringBuilder();
        text.Append(Header).Append("\r\n");
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            text.Append(i + 1).Append(';').Append(CsvCells.Quote(l.EmployeeName)).Append(';')
                .Append(Cells(l.Gross, l.SgkEmployee, l.UnemploymentEmployee, l.IncomeTax, l.StampTax, l.Net, l.EmployerSgk, l.EmployerUnemployment)).Append("\r\n");
        }

        var total = Total(lines);
        text.Append(';').Append(CsvCells.Quote("TOPLAM")).Append(';')
            .Append(Cells(total.Gross, total.SgkEmployee, total.UnemploymentEmployee, total.IncomeTax, total.StampTax, total.Net, total.EmployerSgk, total.EmployerUnemployment)).Append("\r\n");
        return text.ToString();
    }

    /// <summary>The column totals - what the declaration's own totals are reconciled against.</summary>
    public static DeclarationLine Total(IReadOnlyList<DeclarationLine> lines) => new(
        "TOPLAM",
        lines.Sum(l => l.Gross), lines.Sum(l => l.SgkEmployee), lines.Sum(l => l.UnemploymentEmployee),
        lines.Sum(l => l.IncomeTax), lines.Sum(l => l.StampTax), lines.Sum(l => l.Net),
        lines.Sum(l => l.EmployerSgk), lines.Sum(l => l.EmployerUnemployment));

    public static byte[] Encode(string csv) => BankPaymentFileBuilder.Encode(csv);

    private static string Cells(params decimal[] values) => string.Join(';', values.Select(CsvCells.Amount));
}
