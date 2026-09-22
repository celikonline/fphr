using System.Globalization;

namespace Sense.Modules.Payroll.Application.Declarations;

/// <summary>Cell formatting shared by the payroll CSV exports (bank list, declaration summary): quoted text and dot-decimal amounts.</summary>
internal static class CsvCells
{
    /// <summary>Quotes a text cell; a leading = + - @ (or tab/CR) would make a spreadsheet run it as a formula, so it gets a leading apostrophe.</summary>
    public static string Quote(string value)
    {
        var safe = value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal) + "\"";
    }

    /// <summary>An amount with two decimals and a dot, whatever the server's culture.</summary>
    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
