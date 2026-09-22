using System.Globalization;

namespace Sense.Modules.Payroll.Domain.Payments;

/// <summary>One row of the bank's dönüş (receipt) file: which IBAN was paid, how much, and its optional status text.</summary>
public sealed record BankReceiptRow(string Iban, decimal Amount, string? RawStatus);

/// <summary>What the payroll expected to pay one employee, from the bank payment file that was generated for the period.</summary>
public sealed record ExpectedPayment(Guid EmployeeId, string Iban, decimal Amount);

public sealed record BankReceiptMatch(Guid EmployeeId, decimal Amount);

public sealed record BankReceiptAmountMismatch(Guid EmployeeId, decimal ExpectedAmount, decimal ReceiptAmount);

public sealed record BankReceiptRejection(Guid EmployeeId, decimal Amount, string? RawStatus);

/// <summary>
/// <see cref="BankReceiptMatcher.Match"/>'s outcome: every expected payment falls into exactly one bucket -
/// <see cref="Confirmed"/> (IBAN and amount matched, no rejection status), <see cref="AmountMismatches"/> (IBAN
/// found but the amount differs beyond kuruş rounding), <see cref="Rejected"/> (IBAN and amount matched but the
/// bank's own status text says the payment failed), or <see cref="UnmatchedEmployeeIds"/> (no row for that IBAN at
/// all - the bank file may not have reached the bank, or the return file is incomplete).
/// <see cref="UnrecognizedRowCount"/> counts receipt rows whose IBAN matches no expected payment (typically a
/// pasted header row, or a payment for a period/run this dönem's file didn't include).
/// </summary>
public sealed record BankReceiptMatchResult(
    IReadOnlyList<BankReceiptMatch> Confirmed,
    IReadOnlyList<BankReceiptAmountMismatch> AmountMismatches,
    IReadOnlyList<BankReceiptRejection> Rejected,
    IReadOnlyList<Guid> UnmatchedEmployeeIds,
    int UnrecognizedRowCount);

/// <summary>
/// Dekont eşleştirme (bank receipt matching): banks return their own formats, so this only relies on a generic CSV a
/// bookkeeper can produce from any bank's portal export - IBAN, amount and an optional status column, in either
/// order, separated by <c>;</c> or <c>,</c>. It never assumes a specific bank's column layout or header text.
/// </summary>
public static class BankReceiptMatcher
{
    /// <summary>Amounts within one kuruş of each other count as the same payment (rounding across systems).</summary>
    private const decimal Tolerance = 0.01m;

    /// <summary>Turkish and English words a bank's status column uses for a payment that did NOT go through.</summary>
    private static readonly string[] RejectionKeywords =
    [
        "ret", "reddedildi", "hata", "basarisiz", "başarısız", "iade", "iptal", "fail", "error", "rejected", "declined",
    ];

    /// <summary>
    /// Parses the generic CSV: one row per line, <c>;</c> or <c>,</c> separated, first numeric-looking cell is the amount and the
    /// longest cell of at least 15 digits/letters is the IBAN; anything else on the line becomes the status text. A header line (no
    /// cell parses as an amount) is skipped. Blank lines are skipped. Malformed lines (no IBAN-shaped cell) are dropped silently -
    /// they surface later as a lower <see cref="BankReceiptMatchResult.UnrecognizedRowCount"/> than the file's line count, which the
    /// caller can compare against if it wants to warn about the file itself rather than one payment.
    /// </summary>
    public static IReadOnlyList<BankReceiptRow> ParseCsv(string csvContent)
    {
        var rows = new List<BankReceiptRow>();
        foreach (var line in csvContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var cells = line.Contains(';', StringComparison.Ordinal)
                ? line.Split(';')
                : line.Split(',');
            var trimmed = cells.Select(c => c.Trim().Trim('"')).Where(c => c.Length > 0).ToList();
            if (trimmed.Count == 0)
            {
                continue;
            }

            var iban = trimmed.FirstOrDefault(c => c.Replace(" ", string.Empty, StringComparison.Ordinal).Length >= 15
                && c.Count(char.IsLetterOrDigit) == c.Replace(" ", string.Empty, StringComparison.Ordinal).Length);
            if (iban is null)
            {
                continue;
            }

            decimal? amount = null;
            string? status = null;
            foreach (var cell in trimmed)
            {
                if (ReferenceEquals(cell, iban))
                {
                    continue;
                }

                if (amount is null && TryParseAmount(cell, out var parsed))
                {
                    amount = parsed;
                }
                else if (status is null)
                {
                    status = cell;
                }
            }

            if (amount is null)
            {
                // No numeric cell at all: this is the header row (e.g. "IBAN;Tutar;Durum") - not a payment.
                continue;
            }

            rows.Add(new BankReceiptRow(NormalizeIban(iban), amount.Value, status));
        }

        return rows;
    }

    /// <summary>Matches every payment the period expected against the parsed receipt rows; see <see cref="BankReceiptMatchResult"/>.</summary>
    public static BankReceiptMatchResult Match(IReadOnlyList<ExpectedPayment> expected, IReadOnlyList<BankReceiptRow> rows)
    {
        var byIban = rows.ToLookup(r => NormalizeIban(r.Iban));
        var matchedIbans = new HashSet<string>();
        var confirmed = new List<BankReceiptMatch>();
        var mismatches = new List<BankReceiptAmountMismatch>();
        var rejected = new List<BankReceiptRejection>();
        var unmatched = new List<Guid>();

        foreach (var payment in expected)
        {
            var normalizedIban = NormalizeIban(payment.Iban);
            var row = byIban[normalizedIban].FirstOrDefault();
            if (row is null)
            {
                unmatched.Add(payment.EmployeeId);
                continue;
            }

            matchedIbans.Add(normalizedIban);
            if (IsRejected(row.RawStatus))
            {
                rejected.Add(new BankReceiptRejection(payment.EmployeeId, row.Amount, row.RawStatus));
            }
            else if (Math.Abs(row.Amount - payment.Amount) > Tolerance)
            {
                mismatches.Add(new BankReceiptAmountMismatch(payment.EmployeeId, payment.Amount, row.Amount));
            }
            else
            {
                confirmed.Add(new BankReceiptMatch(payment.EmployeeId, row.Amount));
            }
        }

        var unrecognizedRowCount = rows.Select(r => NormalizeIban(r.Iban)).Distinct().Count(iban => !matchedIbans.Contains(iban)
            && expected.All(p => NormalizeIban(p.Iban) != iban));
        return new BankReceiptMatchResult(confirmed, mismatches, rejected, unmatched, unrecognizedRowCount);
    }

    private static bool IsRejected(string? status) =>
        status is not null && RejectionKeywords.Any(k => status.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeIban(string iban) => iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    /// Accepts a dot-decimal amount ("38250.50", this codebase's own export format), a comma-decimal one ("38250,50")
    /// and a thousands-grouped one in either style ("38.250,50" or "38,250.50") - whichever separator appears last is
    /// the decimal point, the other (if any) is a thousands separator and is stripped.
    /// </summary>
    private static bool TryParseAmount(string cell, out decimal amount)
    {
        var lastDot = cell.LastIndexOf('.');
        var lastComma = cell.LastIndexOf(',');
        var normalized = lastComma > lastDot
            ? cell.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal)
            : cell.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
