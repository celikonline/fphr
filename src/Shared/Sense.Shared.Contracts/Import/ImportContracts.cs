namespace Sense.Shared.Contracts.Import;

/// <summary>How matched/unmatched rows are handled (Faz 0: no InsertOnly).</summary>
public enum ImportMode
{
    /// <summary>Matched rows are updated, unmatched rows are inserted.</summary>
    Upsert = 0,

    /// <summary>"Sadece güncelleme modu": unmatched rows are errors, nothing is inserted.</summary>
    UpdateOnly = 1,
}

public enum ImportFailurePolicy
{
    /// <summary>Valid rows are applied, invalid rows are reported.</summary>
    SkipInvalidRows = 0,

    /// <summary>Any row error means nothing is applied.</summary>
    AllOrNothing = 1,
}

public enum ImportColumnDataType
{
    Text = 0,
    Date = 1,
    Email = 2,
    Phone = 3,
    NationalId = 4,
    Lookup = 5,
}

public enum ImportColumnRequirement
{
    /// <summary>Column must be mapped; value required for new records.</summary>
    Always = 0,

    /// <summary>Value required only when a record is inserted.</summary>
    OnInsert = 1,

    Optional = 2,
}

public enum ImportRowOutcome
{
    Inserted = 0,
    Updated = 1,
    Skipped = 2,
    Error = 3,
}

/// <summary>
/// One template column. <see cref="Key"/> is written to the template's hidden key row and used for auto-mapping.
/// Static columns carry a resource key (<see cref="HeaderKey"/>); dynamic columns (e.g. org unit levels) carry tenant data as <see cref="HeaderText"/>.
/// </summary>
public sealed record ImportColumn(
    string Key,
    string? HeaderKey,
    string? HeaderText,
    ImportColumnDataType DataType,
    ImportColumnRequirement Requirement,
    bool IsMatchKey = false,
    IReadOnlyList<string>? Options = null,
    string? DescriptionKey = null)
{
    /// <summary>Must be mapped before a job can run ("IMPORT_UNMAPPED_REQUIRED").</summary>
    public bool IsBlocking => IsMatchKey || Requirement == ImportColumnRequirement.Always;
}

/// <summary>Example row for the template's examples sheet (green = valid, red = invalid); never imported.</summary>
public sealed record ImportSampleRow(bool IsValid, IReadOnlyDictionary<string, string> Values, string NoteKey);

/// <summary>
/// An import template contributed by a module (e.g. Employee's "employee.master"), collected by the DataTransfer module via DI.
/// Texts are resource keys (SharedResource); the DataTransfer module localizes them per requested language.
/// </summary>
public interface IImportTemplate
{
    string Code { get; }

    string NameKey { get; }

    string DescriptionKey { get; }

    /// <summary>Module permission required on top of datatransfer.import.run (e.g. employee.employees.import).</summary>
    string RequiredPermission { get; }

    IReadOnlyList<ImportMode> SupportedModes { get; }

    /// <summary>Template-specific instruction lines (resource keys) appended to the generic rules.</summary>
    IReadOnlyList<string> InstructionKeys { get; }

    /// <summary>Static plus tenant-dynamic columns for the current tenant, in template order.</summary>
    Task<IReadOnlyList<ImportColumn>> GetColumnsAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<ImportSampleRow> GetSampleRows(IReadOnlyList<ImportColumn> columns);
}

/// <summary>A raw cell: its text and, for native spreadsheet date cells, the date value.</summary>
public sealed record ImportCell(string? Text, DateTime? DateValue = null)
{
    public static readonly ImportCell Empty = new(Text: null);

    public bool IsEmpty => DateValue is null && string.IsNullOrWhiteSpace(Text);
}

/// <summary>One data row; <see cref="Values"/> holds only mapped template keys.</summary>
public sealed record ImportRow(int RowNumber, IReadOnlyDictionary<string, ImportCell> Values)
{
    public bool IsMapped(string key) => Values.ContainsKey(key);

    /// <summary>The cell for <paramref name="key"/>, or null when the column is unmapped or the cell is empty.</summary>
    public ImportCell? Get(string key) => Values.TryGetValue(key, out var cell) && !cell.IsEmpty ? cell : null;
}

public sealed record ImportBatch(ImportMode Mode, ImportFailurePolicy FailurePolicy, IReadOnlyCollection<string> MappedKeys, IReadOnlyList<ImportRow> Rows);

/// <summary>Row error: <see cref="Column"/> is the template column key (null = whole row); <see cref="Code"/> is a resource key.</summary>
public sealed record ImportRowError(string? Column, string Code, IReadOnlyDictionary<string, string>? Args = null);

public sealed record ImportRowResult(int RowNumber, ImportRowOutcome Outcome, string? MatchKey, Guid? TargetEntityId, IReadOnlyList<ImportRowError> Errors);

/// <summary>Per-row results; <see cref="Applied"/> is false when nothing was persisted (e.g. AllOrNothing with errors).</summary>
public sealed record ImportBatchResult(IReadOnlyList<ImportRowResult> Rows, bool Applied);

/// <summary>
/// Validates and applies a template's rows inside the owning module. The handler persists its own changes through its
/// module's unit of work (the DataTransfer command's unit of work only covers job bookkeeping). Contract:
/// every row is validated before anything is applied; with <see cref="ImportFailurePolicy.AllOrNothing"/> nothing is
/// persisted if any row fails, otherwise valid rows are persisted together in one save.
/// </summary>
public interface IImportRowHandler
{
    string TemplateCode { get; }

    Task<ImportBatchResult> ProcessAsync(ImportBatch batch, CancellationToken cancellationToken = default);
}

/// <summary>Row error codes shared by all templates (also the SharedResource keys of their messages).</summary>
public static class ImportErrorCodes
{
    public const string UnmappedRequired = "IMPORT_UNMAPPED_REQUIRED";
    public const string RequiredMissing = "IMPORT_REQUIRED_MISSING";
    public const string InvalidNationalId = "IMPORT_INVALID_NATIONAL_ID";
    public const string InvalidDate = "IMPORT_INVALID_DATE";
    public const string InvalidEmail = "IMPORT_INVALID_EMAIL";
    public const string InvalidPhone = "IMPORT_INVALID_PHONE";
    public const string ValueTooLong = "IMPORT_VALUE_TOO_LONG";
    public const string DuplicateRow = "IMPORT_DUPLICATE_ROW";
    public const string NameMismatch = "IMPORT_NAME_MISMATCH";
    public const string NoMatch = "IMPORT_NO_MATCH";
    public const string LookupNotFound = "IMPORT_LOOKUP_NOT_FOUND";
    public const string AmbiguousMatch = "IMPORT_AMBIGUOUS_MATCH";
    public const string OutOfScope = "IMPORT_OUT_OF_SCOPE";

    /// <summary>Arg name of <see cref="UnmappedRequired"/>: comma-separated missing column keys.</summary>
    public const string MissingArg = "missing";

    public const string ValueArg = "value";
    public const string RowArg = "row";
}

/// <summary>Faz 0 limits (ImportOptions in the design doc).</summary>
public static class ImportLimits
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxRows = 20_000;
    public const int SampleCount = 3;
}
