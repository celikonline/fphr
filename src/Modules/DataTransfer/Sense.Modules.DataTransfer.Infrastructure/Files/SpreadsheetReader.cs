using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Sense.Modules.DataTransfer.Application;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Infrastructure;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.DataTransfer.Infrastructure.Files;

/// <summary>Reads the first worksheet of an xlsx (ClosedXML) or a CSV (CsvHelper, delimiter detected, UTF-8 or Windows-1254).</summary>
public sealed class SpreadsheetReader : ISpreadsheetReader
{
    private const int TurkishWindowsCodePage = 1254;
    private const string DateFormat = "dd.MM.yyyy";
    private const string DateTimeFormat = "dd.MM.yyyy HH:mm";
    private static readonly string[] CsvDelimiters = [";", ",", "\t"];

    static SpreadsheetReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public Result<Spreadsheet> Read(byte[] content, ImportFileFormat format, int maxRows)
    {
        try
        {
            return format == ImportFileFormat.Xlsx ? ReadXlsx(content, maxRows) : ReadCsv(content, maxRows);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Error.Rule(DataTransferErrors.FileUnreadable);
        }
    }

    public static string FormatDate(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero ? value.ToString(DateFormat, CultureInfo.InvariantCulture) : value.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    private static Result<Spreadsheet> ReadXlsx(byte[] content, int maxRows)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault();
        if (sheet is null)
        {
            return Error.Rule(DataTransferErrors.FileEmpty);
        }

        var headers = new List<string>();
        var rows = new List<SpreadsheetRow>();
        foreach (var row in sheet.RowsUsed(XLCellsUsedOptions.Contents))
        {
            var lastColumn = row.LastCellUsed(XLCellsUsedOptions.Contents)?.Address.ColumnNumber ?? 0;
            var cells = new List<ImportCell>(lastColumn);
            for (var column = 1; column <= lastColumn; column++)
            {
                cells.Add(ToCell(row.Cell(column)));
            }

            if (row.RowNumber() == 1)
            {
                headers.AddRange(cells.Select(c => c.Text?.Trim() ?? string.Empty));
                continue;
            }

            if (cells.All(c => c.IsEmpty))
            {
                continue;
            }

            rows.Add(new SpreadsheetRow(row.RowNumber(), cells));
            if (rows.Count >= maxRows)
            {
                break;
            }
        }

        return new Spreadsheet(headers, rows);
    }

    private static ImportCell ToCell(IXLCell cell)
    {
        var value = cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => ImportCell.Empty,
            XLDataType.DateTime => new ImportCell(FormatDate(value.GetDateTime()), value.GetDateTime()),
            XLDataType.Number => new ImportCell(FormatNumber(value.GetNumber())),
            XLDataType.Text => new ImportCell(value.GetText()),
            XLDataType.Boolean => new ImportCell(value.GetBoolean() ? "true" : "false"),
            XLDataType.TimeSpan => new ImportCell(value.GetTimeSpan().ToString(null, CultureInfo.InvariantCulture)),
            _ => new ImportCell(cell.GetFormattedString()),
        };
    }

    /// <summary>Integral numbers (national ids, phones typed as numbers) without exponent or separators.</summary>
    private static string FormatNumber(double number) =>
        Math.Abs(number % 1) < double.Epsilon && Math.Abs(number) < 1e15
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);

    private static Result<Spreadsheet> ReadCsv(byte[] content, int maxRows)
    {
        var text = Decode(content);
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            DetectDelimiter = true,
            DetectDelimiterValues = CsvDelimiters,
            BadDataFound = null,
            MissingFieldFound = null,
            IgnoreBlankLines = true,
        };

        using var reader = new StringReader(text);
        using var parser = new CsvParser(reader, configuration);
        var headers = new List<string>();
        var rows = new List<SpreadsheetRow>();
        var recordNumber = 0;
        while (parser.Read())
        {
            recordNumber++;
            var cells = (parser.Record ?? []).Select(v => string.IsNullOrEmpty(v) ? ImportCell.Empty : new ImportCell(v)).ToList();
            if (recordNumber == 1)
            {
                headers.AddRange(cells.Select(c => c.Text?.Trim() ?? string.Empty));
                continue;
            }

            if (cells.All(c => c.IsEmpty))
            {
                continue;
            }

            rows.Add(new SpreadsheetRow(recordNumber, cells));
            if (rows.Count >= maxRows)
            {
                break;
            }
        }

        return new Spreadsheet(headers, rows);
    }

    private static string Decode(byte[] content)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = utf8.GetString(content);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(TurkishWindowsCodePage).GetString(content);
        }
    }
}

/// <summary>SharedResource texts in an explicit culture (template/result files follow ?lang=, not the request culture).</summary>
public sealed class ResourceImportLocalizer : IImportLocalizer
{
    private static readonly System.Resources.ResourceManager Resources =
        new(typeof(SharedResource).Namespace + ".Resources." + nameof(SharedResource), typeof(SharedResource).Assembly);

    public string Get(string key, CultureInfo culture, IReadOnlyDictionary<string, string>? args = null)
    {
        var text = Resources.GetString(key, culture) ?? key;
        if (args is null)
        {
            return text;
        }

        foreach (var (name, value) in args)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return text;
    }
}
