using System.Globalization;
using System.Text.RegularExpressions;
using Sense.Shared.Kernel.ValueObjects;

namespace Sense.Shared.Contracts.Import;

/// <summary>Cell parsing rules shared by import templates (dates, national id, e-mail, option/lookup text matching).</summary>
public static partial class ImportValueParser
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Accepted textual date formats; native spreadsheet date cells are accepted as is. Textual month names are rejected.</summary>
    public static readonly IReadOnlyList<string> DateFormats =
    [
        "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd",
        "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm",
        "dd.MM.yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
    ];

    private static readonly string[] DateFormatArray = [.. DateFormats];

    public static bool TryParseDate(ImportCell cell, out DateOnly date)
    {
        if (cell.DateValue is { } native)
        {
            date = DateOnly.FromDateTime(native);
            return true;
        }

        date = default;
        var text = cell.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (DateTime.TryParseExact(text, DateFormatArray, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        return false;
    }

    /// <summary>Digits only after trimming; spaces inside the number are tolerated.</summary>
    public static bool TryParseNationalId(string? text, out string nationalId)
    {
        nationalId = WhitespaceRegex().Replace(text ?? string.Empty, string.Empty);
        return TcKimlik.IsValid(nationalId);
    }

    public static bool IsValidEmail(string? text) => EmailAddress.IsValid(text?.Trim());

    /// <summary>Trim + collapse inner whitespace (used before any comparison).</summary>
    public static string Normalize(string? text) => WhitespaceRegex().Replace(text ?? string.Empty, " ").Trim();

    /// <summary>
    /// Option/lookup/header matching: case-insensitive and whitespace-insensitive. Casing follows Turkish rules, with dotted and
    /// dotless I folded together so English texts ("ID" / "id") and Turkish texts ("IŞIK" / "ışık") both match.
    /// </summary>
    public static bool TextEquals(string? left, string? right) => string.Equals(MatchKey(left), MatchKey(right), StringComparison.Ordinal);

    /// <summary>Case/whitespace-insensitive key for dictionaries built from option/lookup texts (see <see cref="TextEquals"/>).</summary>
    public static string MatchKey(string? text) => Normalize(text).ToUpper(Turkish).Replace('İ', 'I');

    /// <summary>The option equal to <paramref name="text"/> (case/whitespace-insensitive), or null.</summary>
    public static string? MatchOption(string? text, IEnumerable<string> options) => options.FirstOrDefault(o => TextEquals(o, text));

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
