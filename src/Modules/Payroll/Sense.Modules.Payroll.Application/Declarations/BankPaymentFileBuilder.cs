using System.Globalization;
using System.Text;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>One payment of the bank file: who is paid, to which IBAN, how much.</summary>
public sealed record BankPayment(string EmployeeName, string Iban, decimal Amount, string? BankName);

/// <summary>
/// Builds the generic bank payment list (EFT/havale listesi) of a payroll period as a semicolon-separated CSV that opens in Excel and
/// can be mapped to a bank's own upload format: SiraNo;AdSoyad;IBAN;Banka;Tutar;ParaBirimi;Aciklama. Amounts use a dot decimal so
/// no bank parser trips on a locale; names and text are quoted, and a value that a spreadsheet would run as a formula is neutralised.
/// Bank-specific layouts (fixed-width, XML) are adapters over the same <see cref="BankPayment"/> list, not part of this generic file.
/// </summary>
public static class BankPaymentFileBuilder
{
    public const string Header = "SiraNo;AdSoyad;IBAN;Banka;Tutar;ParaBirimi;Aciklama";

    private const string Currency = "TRY";

    /// <summary>The CSV text, header first, one line per payment (in the given order).</summary>
    public static string Build(IReadOnlyList<BankPayment> payments, string description)
    {
        var text = new StringBuilder();
        text.Append(Header).Append("\r\n");
        for (var i = 0; i < payments.Count; i++)
        {
            var p = payments[i];
            text.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(CsvCells.Quote(p.EmployeeName)).Append(';')
                .Append(p.Iban.Replace(" ", string.Empty, StringComparison.Ordinal)).Append(';')
                .Append(CsvCells.Quote(p.BankName ?? string.Empty)).Append(';')
                .Append(CsvCells.Amount(p.Amount)).Append(';')
                .Append(Currency).Append(';')
                .Append(CsvCells.Quote(description)).Append("\r\n");
        }

        return text.ToString();
    }

    /// <summary>Sum of the payments, rounded to kuruş - what the file's own total must equal the payroll net total to.</summary>
    public static decimal Total(IReadOnlyList<BankPayment> payments) => decimal.Round(payments.Sum(p => p.Amount), 2, MidpointRounding.AwayFromZero);

    /// <summary>UTF-8 with a BOM, so Excel and most bank portals read the Turkish characters right.</summary>
    public static byte[] Encode(string csv) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
}
