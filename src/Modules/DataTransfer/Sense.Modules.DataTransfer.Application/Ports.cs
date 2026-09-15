using System.Globalization;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.DataTransfer.Application;

/// <summary>Application layer error codes (localized via SharedResource).</summary>
public static class DataTransferErrors
{
    public const string TemplateNotFound = "datatransfer.template_not_found";
    public const string JobNotFound = "datatransfer.job_not_found";
    public const string JobNotFinished = "datatransfer.job_not_finished";
    public const string FileRequired = "datatransfer.file_required";
    public const string FileTooLarge = "datatransfer.file_too_large";
    public const string FileTypeNotSupported = "datatransfer.file_type_not_supported";
    public const string FileUnreadable = "datatransfer.file_unreadable";
    public const string FileEmpty = "datatransfer.file_empty";
    public const string TooManyRows = "datatransfer.too_many_rows";
    public const string MappingInvalid = "datatransfer.mapping_invalid";
    public const string ModeInvalid = "datatransfer.mode_invalid";
    public const string ModeNotSupported = "datatransfer.mode_not_supported";
    public const string FailurePolicyInvalid = "datatransfer.failure_policy_invalid";
    public const string OutcomeInvalid = "datatransfer.outcome_invalid";
    public const string RunFailed = "datatransfer.run_failed";
}

/// <summary>One non-empty sheet row (1-based sheet row number); cells indexed by 0-based column position.</summary>
public sealed record SpreadsheetRow(int RowNumber, IReadOnlyList<ImportCell> Cells)
{
    public ImportCell Cell(int index) => index >= 0 && index < Cells.Count ? Cells[index] : ImportCell.Empty;
}

/// <summary>First worksheet (or CSV): header row texts and the non-empty rows below it.</summary>
public sealed record Spreadsheet(IReadOnlyList<string> Headers, IReadOnlyList<SpreadsheetRow> Rows)
{
    public int ColumnCount => Math.Max(Headers.Count, Rows.Count == 0 ? 0 : Rows.Max(r => r.Cells.Count));
}

/// <summary>Reads uploaded xlsx/csv files (ClosedXML/CsvHelper in Infrastructure).</summary>
public interface ISpreadsheetReader
{
    /// <summary>Fails with <see cref="DataTransferErrors.FileUnreadable"/> for corrupt files; stops collecting after <paramref name="maxRows"/> rows.</summary>
    Result<Spreadsheet> Read(byte[] content, ImportFileFormat format, int maxRows);
}

public sealed record TemplateWorkbookColumn(ImportColumn Column, string Header, string? Description, string RequirementLabel);

public sealed record TemplateWorkbookLabels(
    string InstructionsSheet,
    string ExamplesSheet,
    string ListsSheet,
    string InstructionsTitle,
    string ColumnsTitle,
    string ColumnHeader,
    string RequirementHeader,
    string DescriptionHeader,
    string ExampleNoteHeader);

public sealed record TemplateWorkbookSample(bool IsValid, IReadOnlyDictionary<string, string> Values, string Note);

public sealed record TemplateWorkbookModel(
    string DataSheetName,
    IReadOnlyList<TemplateWorkbookColumn> Columns,
    IReadOnlyList<string> Instructions,
    IReadOnlyList<TemplateWorkbookSample> Samples,
    TemplateWorkbookLabels Labels);

public sealed record ResultWorkbookRow(int RowNumber, IReadOnlyList<ImportCell> Cells, string? Outcome, string? Errors, bool IsError);

public sealed record ResultWorkbookModel(
    string SheetName,
    IReadOnlyList<string> Headers,
    IReadOnlyList<ImportCell>? KeyRow,
    IReadOnlyList<ResultWorkbookRow> Rows,
    string OutcomeHeader,
    string ErrorHeader);

/// <summary>Builds the downloadable template and result workbooks (ClosedXML in Infrastructure).</summary>
public interface IImportWorkbookWriter
{
    byte[] BuildTemplate(TemplateWorkbookModel model);

    byte[] BuildResult(ResultWorkbookModel model);
}

/// <summary>Resource text in an explicit culture ({name} placeholders replaced from args); returns the key when missing.</summary>
public interface IImportLocalizer
{
    string Get(string key, CultureInfo culture, IReadOnlyDictionary<string, string>? args = null);
}

/// <summary>Read side for job lists and row outcomes.</summary>
public interface IImportJobReadStore
{
    Task<PagedResult<ImportJob>> ListAsync(PagedQuery paging, CancellationToken ct);

    Task<PagedResult<ImportJobRow>> ListRowsAsync(Guid importJobId, ImportJobRowOutcome? outcome, PagedQuery paging, CancellationToken ct);

    Task<IReadOnlyList<ImportJobRow>> ListAllRowsAsync(Guid importJobId, CancellationToken ct);
}

/// <summary>A file returned by a download query.</summary>
public sealed record FileDownload(string FileName, string ContentType, byte[] Content)
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
