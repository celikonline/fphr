using ClosedXML.Excel;
using Sense.Modules.DataTransfer.Application;
using Sense.Shared.Contracts.Import;

namespace Sense.Modules.DataTransfer.Infrastructure.Files;

/// <summary>ClosedXML template/result workbooks.</summary>
public sealed class ImportWorkbookWriter : IImportWorkbookWriter
{
    public const int HeaderRow = 1;
    public const int KeyRow = 2;
    public const int FirstDataRow = 3;

    private const int MaxSheetNameLength = 31;
    private const int MinColumnWidth = 14;
    private const int MaxColumnWidth = 45;
    private const int WidthPadding = 4;
    private const string ExcelDateFormat = "dd.mm.yyyy";
    private const string TextFormat = "@";
    private const string Bullet = "• ";
    private static readonly char[] InvalidSheetNameChars = ['[', ']', ':', '*', '?', '/', '\\'];
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#DDEBF7");
    private static readonly XLColor RequiredHeaderFill = XLColor.FromHtml("#FCE4D6");
    private static readonly XLColor ValidFill = XLColor.FromHtml("#C6EFCE");
    private static readonly XLColor InvalidFill = XLColor.FromHtml("#FFC7CE");

    public byte[] BuildTemplate(TemplateWorkbookModel model)
    {
        using var workbook = new XLWorkbook();
        var data = workbook.Worksheets.Add(SheetName(model.DataSheetName));
        var instructions = workbook.Worksheets.Add(SheetName(model.Labels.InstructionsSheet));
        var examples = workbook.Worksheets.Add(SheetName(model.Labels.ExamplesSheet));
        var lists = workbook.Worksheets.Add(SheetName(model.Labels.ListsSheet));

        WriteDataSheet(data, lists, model.Columns);
        WriteInstructions(instructions, model);
        WriteExamples(examples, model);

        lists.Visibility = XLWorksheetVisibility.Hidden;
        data.SetTabActive();
        return Save(workbook);
    }

    public byte[] BuildResult(ResultWorkbookModel model)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(model.SheetName));
        var width = Math.Max(model.Headers.Count, Math.Max(model.KeyRow?.Count ?? 0, model.Rows.Count == 0 ? 0 : model.Rows.Max(r => r.Cells.Count)));
        var outcomeColumn = width + 1;
        var errorColumn = width + 2;

        for (var i = 0; i < model.Headers.Count; i++)
        {
            sheet.Cell(HeaderRow, i + 1).Value = model.Headers[i];
        }

        sheet.Cell(HeaderRow, outcomeColumn).Value = model.OutcomeHeader;
        sheet.Cell(HeaderRow, errorColumn).Value = model.ErrorHeader;
        StyleHeader(sheet.Range(HeaderRow, 1, HeaderRow, errorColumn), HeaderFill);

        if (model.KeyRow is { } keyRow)
        {
            for (var i = 0; i < keyRow.Count; i++)
            {
                WriteCell(sheet.Cell(KeyRow, i + 1), keyRow[i]);
            }

            sheet.Row(KeyRow).Hide();
        }

        foreach (var row in model.Rows)
        {
            for (var i = 0; i < row.Cells.Count; i++)
            {
                WriteCell(sheet.Cell(row.RowNumber, i + 1), row.Cells[i]);
            }

            sheet.Cell(row.RowNumber, outcomeColumn).Value = row.Outcome ?? string.Empty;
            sheet.Cell(row.RowNumber, errorColumn).Value = row.Errors ?? string.Empty;
            if (row.IsError)
            {
                sheet.Range(row.RowNumber, outcomeColumn, row.RowNumber, errorColumn).Style.Fill.BackgroundColor = InvalidFill;
            }
        }

        for (var column = 1; column <= errorColumn; column++)
        {
            var header = column <= model.Headers.Count ? model.Headers[column - 1] : string.Empty;
            sheet.Column(column).Width = ColumnWidth(header);
        }

        sheet.Column(errorColumn).Width = MaxColumnWidth * 2;
        sheet.SheetView.FreezeRows(HeaderRow);
        return Save(workbook);
    }

    private static void WriteDataSheet(IXLWorksheet data, IXLWorksheet lists, IReadOnlyList<TemplateWorkbookColumn> columns)
    {
        var lastDataRow = FirstDataRow + ImportLimits.MaxRows - 1;
        var listColumn = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var index = i + 1;
            var header = data.Cell(HeaderRow, index);
            header.Value = column.Header;
            StyleHeader(data.Range(HeaderRow, index, HeaderRow, index), column.Column.IsBlocking ? RequiredHeaderFill : HeaderFill);
            if (!string.IsNullOrWhiteSpace(column.Description))
            {
                header.CreateComment().AddText(column.Description);
            }

            data.Cell(KeyRow, index).Value = column.Column.Key;
            data.Column(index).Width = ColumnWidth(column.Header);

            switch (column.Column.DataType)
            {
                case ImportColumnDataType.Date:
                    data.Column(index).Style.NumberFormat.Format = ExcelDateFormat;
                    break;
                case ImportColumnDataType.NationalId or ImportColumnDataType.Phone:
                    data.Column(index).Style.NumberFormat.Format = TextFormat;
                    break;
            }

            if (column.Column.Options is { Count: > 0 } options)
            {
                listColumn++;
                lists.Cell(HeaderRow, listColumn).Value = column.Column.Key;
                for (var o = 0; o < options.Count; o++)
                {
                    lists.Cell(HeaderRow + 1 + o, listColumn).Value = options[o];
                }

                var validation = data.Range(FirstDataRow, index, lastDataRow, index).CreateDataValidation();
                validation.IgnoreBlanks = true;
                validation.List(lists.Range(HeaderRow + 1, listColumn, HeaderRow + options.Count, listColumn), true);
                validation.ShowErrorMessage = true;
                validation.ErrorStyle = XLErrorStyle.Stop;
            }
        }

        data.Row(KeyRow).Hide();
        data.SheetView.FreezeRows(KeyRow);
    }

    private static void WriteInstructions(IXLWorksheet sheet, TemplateWorkbookModel model)
    {
        var row = 1;
        sheet.Cell(row, 1).Value = model.Labels.InstructionsTitle;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        sheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        foreach (var line in model.Instructions)
        {
            sheet.Cell(row, 1).Value = Bullet + line;
            sheet.Cell(row, 1).Style.Alignment.WrapText = true;
            sheet.Range(row, 1, row, 3).Merge();
            sheet.Row(row).Height = 30;
            row++;
        }

        row++;
        sheet.Cell(row, 1).Value = model.Labels.ColumnsTitle;
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;
        sheet.Cell(row, 1).Value = model.Labels.ColumnHeader;
        sheet.Cell(row, 2).Value = model.Labels.RequirementHeader;
        sheet.Cell(row, 3).Value = model.Labels.DescriptionHeader;
        StyleHeader(sheet.Range(row, 1, row, 3), HeaderFill);
        row++;

        foreach (var column in model.Columns)
        {
            sheet.Cell(row, 1).Value = column.Header;
            sheet.Cell(row, 2).Value = column.RequirementLabel;
            sheet.Cell(row, 3).Value = column.Description ?? string.Empty;
            sheet.Cell(row, 3).Style.Alignment.WrapText = true;
            row++;
        }

        sheet.Column(1).Width = 30;
        sheet.Column(2).Width = 22;
        sheet.Column(3).Width = 90;
    }

    private static void WriteExamples(IXLWorksheet sheet, TemplateWorkbookModel model)
    {
        var noteColumn = model.Columns.Count + 1;
        for (var i = 0; i < model.Columns.Count; i++)
        {
            sheet.Cell(HeaderRow, i + 1).Value = model.Columns[i].Header;
            sheet.Column(i + 1).Width = ColumnWidth(model.Columns[i].Header);
        }

        sheet.Cell(HeaderRow, noteColumn).Value = model.Labels.ExampleNoteHeader;
        StyleHeader(sheet.Range(HeaderRow, 1, HeaderRow, noteColumn), HeaderFill);
        sheet.Column(noteColumn).Width = MaxColumnWidth * 2;

        var row = HeaderRow + 1;
        foreach (var sample in model.Samples)
        {
            for (var i = 0; i < model.Columns.Count; i++)
            {
                var cell = sheet.Cell(row, i + 1);
                cell.Style.NumberFormat.Format = TextFormat;
                cell.Value = sample.Values.TryGetValue(model.Columns[i].Column.Key, out var value) ? value : string.Empty;
            }

            sheet.Cell(row, noteColumn).Value = sample.Note;
            sheet.Range(row, 1, row, noteColumn).Style.Fill.BackgroundColor = sample.IsValid ? ValidFill : InvalidFill;
            row++;
        }
    }

    private static void WriteCell(IXLCell cell, ImportCell value)
    {
        if (value.DateValue is { } date)
        {
            cell.Value = date;
            cell.Style.NumberFormat.Format = ExcelDateFormat;
        }
        else if (!string.IsNullOrEmpty(value.Text))
        {
            cell.Value = value.Text;
        }
    }

    private static void StyleHeader(IXLRange range, XLColor fill)
    {
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = fill;
    }

    private static double ColumnWidth(string header) => Math.Clamp(header.Length + WidthPadding, MinColumnWidth, MaxColumnWidth);

    /// <summary>Excel sheet names: at most 31 characters, none of []:*?/\.</summary>
    public static string SheetName(string name)
    {
        var cleaned = new string(name.Select(c => InvalidSheetNameChars.Contains(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length > MaxSheetNameLength ? cleaned[..MaxSheetNameLength] : cleaned.Length == 0 ? "Sheet" : cleaned;
    }

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
