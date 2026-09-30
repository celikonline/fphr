using Sense.Modules.Reporting.Contracts;
using Sense.Modules.Reporting.Domain;
using Sense.Shared.Contracts.Export;
using Sense.Shared.Contracts.Paging;

namespace Sense.Modules.Reporting.Application;

public sealed record ReportExecutionDto(
    Guid Id,
    string ReportKey,
    ReportExecutionStatus Status,
    DateTime RequestedAt,
    DateTime? CompletedAt,
    string? ResultFileName,
    string? ErrorMessage)
{
    public static ReportExecutionDto From(ReportExecution execution) => new(
        execution.Id,
        execution.ReportKey,
        execution.Status,
        execution.RequestedAt,
        execution.CompletedAt,
        execution.ResultFileName,
        execution.ErrorMessage);
}

/// <summary>
/// One report the caller may generate (Kolay İK "rapor türü"): the stable key (web resolves the display name/description
/// from the "reports" i18n namespace), its menu category (<see cref="ReportCategories"/>) and the filters it accepts -
/// the web draws the parameter form from <see cref="Parameters"/> (birim ağacı, yıl, ay, tarih aralığı, anahtar).
/// </summary>
public sealed record ReportCatalogItemDto(string Key, string CategoryKey, IReadOnlyList<ReportParameterDescriptor> Parameters);

public sealed record ReportExecutionFileDto(string FileName, string ContentType, byte[] Content);

/// <summary>One sheet of the report workbook (sheet tabs on the web).</summary>
public sealed record ReportSheetDto(int Index, string Name);

/// <summary>Grid column: the header text and the value type the web formats by ("text", "number", "date", "dateTime", "boolean").</summary>
public sealed record ReportColumnDto(string Header, ExportColumnType Type);

/// <summary>
/// Grid data of a report execution's workbook: every sheet, the requested sheet's columns and one filtered/sorted/paged
/// slice of its rows. Cells are string / number / bool / ISO date ("yyyy-MM-dd") / ISO date-time / null, in column order.
/// </summary>
public sealed record ReportDataDto(
    IReadOnlyList<ReportSheetDto> Sheets,
    int SheetIndex,
    IReadOnlyList<ReportColumnDto> Columns,
    PagedResult<IReadOnlyList<object?>> Rows);
