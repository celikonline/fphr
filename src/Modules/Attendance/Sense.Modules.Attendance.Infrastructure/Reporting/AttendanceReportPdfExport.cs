using Sense.Modules.Reporting.Contracts;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Attendance.Infrastructure.Reporting;

/// <summary>
/// TK-004: Attendance zaman raporlarının (aylık özet/kapanış ve günlük/durum raporları) Excel'e ek PDF çıktı seçeneği. Sağlayıcının
/// <c>IReportProvider.Parameters</c>'ta duyurmadığı, isteğe bağlı "format" parametresini okur (bkz.
/// ReportParameterValidation: "Unknown extra keys are ignored" - sağlayıcı duyurduğundan fazlasını kabul edebilir);
/// "pdf" verilirse PDF, aksi halde (parametre yok/başka değer) varsayılan mevcut Excel davranışı değişmeden çalışır.
/// PDF'e giden veri Excel'e giden ExportColumn/satır listesiyle birebir aynıdır - iki biçim aynı kaynaktan üretilir.
/// Render Shared'ın ISimplePdfWriter'ı (Payroll ücret pusulası, Employee çalışma belgesinin de kullandığı QuestPDF
/// altyapısı) üzerinden yapılır; yeni bir PDF kütüphanesi eklenmedi.
/// </summary>
public static class AttendanceReportPdfExport
{
    private const string FormatParameterKey = "format";
    private const string PdfFormatValue = "pdf";

    public static bool IsPdfRequested(IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue(FormatParameterKey, out var value) && string.Equals(value, PdfFormatValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>Excel'e giden ExportColumn/satır verisinden PDF belgesini kurar. Saf - ISimplePdfWriter'ı çağırmaz, bu yüzden QuestPDF render'ı olmadan test edilebilir.</summary>
    public static SimplePdfDocument BuildDocument(
        string title,
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        DateOnly periodFrom,
        DateOnly periodTo) =>
        BuildDocument(title, columns, rows, FormatPeriod(periodFrom, periodTo));

    /// <summary>Genel biçim: dönem alt yazısı çağıran tarafından verilir (tek gün, aralık ya da dönemsiz raporlar için).</summary>
    public static SimplePdfDocument BuildDocument(
        string title,
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        string periodText)
    {
        var tableColumns = columns.Select(c => c.Header).ToList();
        var tableRows = rows.Select(row => (IReadOnlyList<string>)row.Select(FormatCell).ToList()).ToList();
        var rightAlignedColumns = columns
            .Select((column, index) => (column, index))
            .Where(x => x.column.Type == ExportColumnType.Number)
            .Select(x => x.index)
            .ToList();

        return new SimplePdfDocument(
            title,
            null,
            [new SimplePdfTable(null, tableColumns, tableRows, rightAlignedColumns)],
            periodText);
    }

    public static string FormatPeriod(DateOnly from, DateOnly to) =>
        from == to ? $"{from:dd.MM.yyyy}" : $"{from:dd.MM.yyyy} - {to:dd.MM.yyyy}";

    /// <summary>
    /// Tüm zaman raporlarının ortak çıkış yolu: "format=pdf" ise PDF, aksi halde Excel. Aynı ExportColumn/satır verisi iki biçime de gider;
    /// boş satır kümesi yalnız başlık satırlı dosya üretir.
    /// </summary>
    public static ReportFileResult Produce(
        IReadOnlyDictionary<string, string> parameters,
        IReportExporter exporter,
        ISimplePdfWriter pdfWriter,
        string sheetName,
        string fileNameStem,
        IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        string periodText,
        CancellationToken cancellationToken)
    {
        if (IsPdfRequested(parameters))
        {
            return Write(pdfWriter, BuildDocument(sheetName, columns, rows, periodText), fileNameStem);
        }

        var file = exporter.ExportToExcel($"{fileNameStem}.xlsx", [new ExportTable(sheetName, columns, rows)], cancellationToken);
        return new ReportFileResult(file.FileName, file.ContentType, file.Content);
    }

    public static ReportFileResult Write(ISimplePdfWriter writer, SimplePdfDocument document, string fileNameStem) =>
        new($"{fileNameStem}.pdf", "application/pdf", writer.Write(document));

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime dateTime => dateTime.ToString("dd.MM.yyyy HH:mm"),
        DateOnly date => date.ToString("dd.MM.yyyy"),
        decimal number => number.ToString("0.##"),
        _ => value.ToString() ?? string.Empty,
    };
}
