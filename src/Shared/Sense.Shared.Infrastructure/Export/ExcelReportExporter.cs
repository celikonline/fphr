using System.Globalization;
using ClosedXML.Excel;
using Sense.Shared.Contracts.Export;

namespace Sense.Shared.Infrastructure.Export;

/// <summary>
/// ClosedXML tabanlı <see cref="IReportExporter"/> uygulaması. DataTransfer modülünün import şablon/sonuç
/// workbook'larından (<c>ImportWorkbookWriter</c>) kasıtlı olarak bağımsızdır - orası içe aktarma şablonuna özel
/// kalır (sabit sütun/örnek/talimat sayfaları), bu sınıf ise herhangi bir modülün "tabloyu Excel'e aktar"
/// ihtiyacı için genel, sayfa/sütun/satır tabanlı bir yazıcıdır.
/// </summary>
public sealed class ExcelReportExporter : IReportExporter
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int MinColumnWidth = 10;
    private const int MaxColumnWidth = 45;
    private const int WidthPadding = 4;
    private const int MaxSheetNameLength = 31;
    private const string DateFormat = "dd.mm.yyyy";
    private const string DateTimeFormat = "dd.mm.yyyy hh:mm";
    private const string DefaultSheetName = "Sayfa1";
    private static readonly char[] InvalidSheetNameChars = ['[', ']', ':', '*', '?', '/', '\\'];
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#DDEBF7");

    public ExportFileResult ExportToExcel(ExportTable table, CancellationToken cancellationToken = default) =>
        ExportToExcel(SanitizeSheetName(table.SheetName, []) + ".xlsx", [table], cancellationToken);

    public ExportFileResult ExportToExcel(string fileName, IReadOnlyList<ExportTable> sheets, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (sheets.Count == 0)
        {
            throw new ArgumentException("En az bir sayfa gerekir.", nameof(sheets));
        }

        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteSheet(workbook, table, usedNames);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExportFileResult(fileName, ExcelContentType, stream.ToArray());
    }

    private static void WriteSheet(XLWorkbook workbook, ExportTable table, HashSet<string> usedNames)
    {
        var sheet = workbook.Worksheets.Add(SanitizeSheetName(table.SheetName, usedNames));
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var column = table.Columns[c];
            var cell = sheet.Cell(1, c + 1);
            cell.Value = column.Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            sheet.Column(c + 1).Width = ColumnWidth(column.Header);
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < table.Columns.Count && c < row.Count; c++)
            {
                WriteCell(sheet.Cell(r + 2, c + 1), row[c], table.Columns[c].Type);
            }
        }

        sheet.SheetView.FreezeRows(1);
    }

    private static void WriteCell(IXLCell cell, object? value, ExportColumnType type)
    {
        switch (value)
        {
            case null:
                return;
            case bool boolValue:
                cell.Value = boolValue;
                return;
            case DateOnly dateOnly:
                cell.Value = dateOnly.ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                return;
            case DateTime dateTime:
                cell.Value = dateTime;
                cell.Style.NumberFormat.Format = type == ExportColumnType.Date ? DateFormat : DateTimeFormat;
                return;
        }

        if (type == ExportColumnType.Number && value is IConvertible convertible)
        {
            cell.Value = convertible.ToDouble(CultureInfo.InvariantCulture);
            return;
        }

        cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static double ColumnWidth(string header) => Math.Clamp(header.Length + WidthPadding, MinColumnWidth, MaxColumnWidth);

    /// <summary>Excel sayfa adı kuralları: en fazla 31 karakter, []:*?/\ yok, boşsa ve çakışıyorsa numaralandırılır.</summary>
    private static string SanitizeSheetName(string name, HashSet<string> usedNames)
    {
        var cleaned = new string(name.Select(c => InvalidSheetNameChars.Contains(c) ? ' ' : c).ToArray()).Trim();
        var baseName = cleaned.Length == 0 ? DefaultSheetName : cleaned.Length > MaxSheetNameLength ? cleaned[..MaxSheetNameLength] : cleaned;

        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            var suffixText = suffix.ToString(CultureInfo.InvariantCulture);
            candidate = baseName[..Math.Min(baseName.Length, MaxSheetNameLength - suffixText.Length - 1)] + " " + suffixText;
            suffix++;
        }

        return candidate;
    }
}
