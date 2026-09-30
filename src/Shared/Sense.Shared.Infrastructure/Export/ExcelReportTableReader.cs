using System.Globalization;
using ClosedXML.Excel;
using Sense.Shared.Contracts.Export;

namespace Sense.Shared.Infrastructure.Export;

/// <summary>ClosedXML tabanlı <see cref="IReportTableReader"/>; <see cref="ExcelReportExporter"/>'ın yazdığı biçimi (1. satır başlık) okur.</summary>
public sealed class ExcelReportTableReader : IReportTableReader
{
    private const int HeaderRow = 1;

    public IReadOnlyList<ExportTable> Read(byte[] content, CancellationToken cancellationToken = default)
    {
        try
        {
            using var workbook = new XLWorkbook(new MemoryStream(content));
            var tables = new List<ExportTable>();
            foreach (var sheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tables.Add(ReadSheet(sheet));
            }

            return tables;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("Dosya okunabilir bir Excel çalışma kitabı değil.", ex);
        }
    }

    private static ExportTable ReadSheet(IXLWorksheet sheet)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        var rows = new List<object?[]>();
        for (var r = HeaderRow + 1; r <= lastRow; r++)
        {
            var cells = new object?[lastColumn];
            for (var c = 0; c < lastColumn; c++)
            {
                cells[c] = ReadValue(sheet.Cell(r, c + 1));
            }

            if (cells.Any(value => value is not null))
            {
                rows.Add(cells);
            }
        }

        var columns = new List<ExportColumn>(lastColumn);
        for (var c = 0; c < lastColumn; c++)
        {
            var type = ColumnTypeOf(rows, c);
            columns.Add(new ExportColumn(sheet.Cell(HeaderRow, c + 1).GetString(), type));
            if (type == ExportColumnType.Text)
            {
                foreach (var row in rows)
                {
                    row[c] = row[c] is null or string ? row[c] : ToText(row[c]!);
                }
            }
        }

        return new ExportTable(sheet.Name, columns, rows.Select(row => (IReadOnlyList<object?>)row).ToList());
    }

    private static object? ReadValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        switch (cell.DataType)
        {
            case XLDataType.Boolean:
                return cell.GetBoolean();
            case XLDataType.Number:
                return cell.GetDouble();
            case XLDataType.DateTime:
            {
                var dateTime = cell.GetDateTime();
                return HasTimeFormat(cell) ? dateTime : DateOnly.FromDateTime(dateTime);
            }

            default:
            {
                var text = cell.GetString();
                return text.Length == 0 ? null : text;
            }
        }
    }

    /// <summary><see cref="ExcelReportExporter"/> tarih-saat hücrelerine "dd.mm.yyyy hh:mm", yalnız tarihe "dd.mm.yyyy" biçimi yazar.</summary>
    private static bool HasTimeFormat(IXLCell cell) =>
        cell.Style.NumberFormat.Format.Contains('h', StringComparison.OrdinalIgnoreCase);

    private static ExportColumnType ColumnTypeOf(List<object?[]> rows, int column)
    {
        ExportColumnType? type = null;
        foreach (var row in rows)
        {
            if (row[column] is not { } value)
            {
                continue;
            }

            var current = TypeOf(value);
            if (type is null)
            {
                type = current;
            }
            else if (type != current)
            {
                return ExportColumnType.Text;
            }
        }

        return type ?? ExportColumnType.Text;
    }

    private static ExportColumnType TypeOf(object value) => value switch
    {
        bool => ExportColumnType.Boolean,
        double => ExportColumnType.Number,
        DateOnly => ExportColumnType.Date,
        DateTime => ExportColumnType.DateTime,
        _ => ExportColumnType.Text,
    };

    private static string ToText(object value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        double number => number.ToString("0.############", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };
}
