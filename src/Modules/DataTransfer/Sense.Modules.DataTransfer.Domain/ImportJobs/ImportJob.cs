using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.DataTransfer.Domain.ImportJobs;

public enum ImportJobStatus
{
    Uploaded = 0,
    Mapped = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4,
}

public enum ImportJobMode
{
    Upsert = 0,
    UpdateOnly = 1,
}

public enum ImportJobFailurePolicy
{
    SkipInvalidRows = 0,
    AllOrNothing = 1,
}

public enum ImportJobRowOutcome
{
    Inserted = 0,
    Updated = 1,
    Skipped = 2,
    Error = 3,
}

public enum ImportFileFormat
{
    Xlsx = 0,
    Csv = 1,
}

/// <summary>A column of the uploaded file with sample values and the auto-mapping suggestion.</summary>
public sealed record ImportSourceColumn(int Index, string Header, IReadOnlyList<string> Samples, string? SuggestedTargetKey, bool AutoMatched);

/// <summary>Source column → template column key (null = ignored).</summary>
public sealed record ImportColumnMapping(int SourceIndex, string? TargetKey);

public sealed record ImportJobRowError(string? Column, string Code, IReadOnlyDictionary<string, string>? Args);

public sealed record ImportJobCounts(int Inserted, int Updated, int Skipped, int Errors);

/// <summary>
/// One upload of a file against an import template: Uploaded (analyzed) → Mapped (columns + options saved) → Completed /
/// CompletedWithErrors / Failed. Runs synchronously in Faz 0.
/// </summary>
public sealed class ImportJob : TenantAggregateRoot<Guid>
{
    private ImportJob()
    {
    }

    private ImportJob(Guid id, Guid tenantId)
        : base(id, tenantId)
    {
    }

    public string TemplateCode { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public ImportFileFormat Format { get; private set; }

    public ImportJobStatus Status { get; private set; }

    public ImportJobMode Mode { get; private set; }

    public ImportJobFailurePolicy FailurePolicy { get; private set; }

    /// <summary>True when row 2 of the file is the template's (hidden) column key row; data then starts at row 3.</summary>
    public bool HasKeyRow { get; private set; }

    public int TotalRows { get; private set; }

    public int InsertedCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int SkippedCount { get; private set; }

    public int ErrorCount { get; private set; }

    public IReadOnlyList<ImportSourceColumn> SourceColumns { get; private set; } = [];

    public IReadOnlyList<ImportColumnMapping> Mapping { get; private set; } = [];

    public Guid? RequestedByUserId { get; private set; }

    public string? RequestedByName { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    /// <summary>Error code when the whole run failed unexpectedly (not a row error).</summary>
    public string? FailureCode { get; private set; }

    public bool IsFinished => Status is ImportJobStatus.Completed or ImportJobStatus.CompletedWithErrors or ImportJobStatus.Failed;

    public static ImportJob Create(
        Guid tenantId,
        string templateCode,
        string fileName,
        ImportFileFormat format,
        bool hasKeyRow,
        int totalRows,
        IReadOnlyList<ImportSourceColumn> sourceColumns,
        Guid? requestedByUserId,
        string? requestedByName)
    {
        return new ImportJob(Guid.CreateVersion7(), tenantId)
        {
            TemplateCode = templateCode,
            FileName = fileName,
            Format = format,
            HasKeyRow = hasKeyRow,
            TotalRows = totalRows,
            SourceColumns = sourceColumns,
            Mapping = sourceColumns.Select(c => new ImportColumnMapping(c.Index, c.SuggestedTargetKey)).ToList(),
            Status = ImportJobStatus.Uploaded,
            Mode = ImportJobMode.Upsert,
            FailurePolicy = ImportJobFailurePolicy.SkipInvalidRows,
            RequestedByUserId = requestedByUserId,
            RequestedByName = requestedByName,
        };
    }

    public Result EnsureEditable() =>
        Status is ImportJobStatus.Uploaded or ImportJobStatus.Mapped ? Result.Success() : Error.Conflict(DataTransferDomainErrors.JobNotEditable);

    public Result SetMapping(IReadOnlyList<ImportColumnMapping> mapping, ImportJobMode mode, ImportJobFailurePolicy failurePolicy)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        Mapping = mapping;
        Mode = mode;
        FailurePolicy = failurePolicy;
        Status = ImportJobStatus.Mapped;
        return Result.Success();
    }

    public Result EnsureRunnable() =>
        Status == ImportJobStatus.Mapped ? Result.Success() : Error.Conflict(DataTransferDomainErrors.JobNotRunnable);

    public void Start(DateTime now) => StartedAt = now;

    /// <summary>Records the row outcomes: AllOrNothing with errors → Failed, errors → CompletedWithErrors, else Completed.</summary>
    public void Finish(ImportJobCounts counts, bool applied, DateTime now)
    {
        InsertedCount = counts.Inserted;
        UpdatedCount = counts.Updated;
        SkippedCount = counts.Skipped;
        ErrorCount = counts.Errors;
        FinishedAt = now;
        Status = !applied || (FailurePolicy == ImportJobFailurePolicy.AllOrNothing && counts.Errors > 0)
            ? ImportJobStatus.Failed
            : counts.Errors > 0 ? ImportJobStatus.CompletedWithErrors : ImportJobStatus.Completed;
    }

    public void Fail(string failureCode, DateTime now)
    {
        FailureCode = failureCode;
        InsertedCount = UpdatedCount = SkippedCount = ErrorCount = 0;
        FinishedAt = now;
        Status = ImportJobStatus.Failed;
    }
}

/// <summary>Outcome of one data row (row number = the row in the uploaded sheet).</summary>
public sealed class ImportJobRow : TenantEntity<Guid>
{
    private ImportJobRow()
    {
    }

    private ImportJobRow(Guid id, Guid tenantId)
        : base(id, tenantId)
    {
    }

    public Guid ImportJobId { get; private set; }

    public int RowNumber { get; private set; }

    public ImportJobRowOutcome Outcome { get; private set; }

    public string? MatchKey { get; private set; }

    public Guid? TargetEntityId { get; private set; }

    public IReadOnlyList<ImportJobRowError> Errors { get; private set; } = [];

    public static ImportJobRow Create(Guid tenantId, Guid importJobId, int rowNumber, ImportJobRowOutcome outcome, string? matchKey, Guid? targetEntityId, IReadOnlyList<ImportJobRowError> errors) =>
        new(Guid.CreateVersion7(), tenantId)
        {
            ImportJobId = importJobId,
            RowNumber = rowNumber,
            Outcome = outcome,
            MatchKey = matchKey,
            TargetEntityId = targetEntityId,
            Errors = errors,
        };
}

/// <summary>
/// Uploaded source file kept as a bytea row (Faz 0: IFileStorage only offers presigned URLs, and the file must be re-read on run
/// and for the result file). Result files are generated on demand from this file and the stored row outcomes.
/// </summary>
public sealed class ImportSourceFile : TenantEntity<Guid>
{
    private ImportSourceFile()
    {
    }

    private ImportSourceFile(Guid id, Guid tenantId)
        : base(id, tenantId)
    {
    }

    public Guid ImportJobId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public byte[] Content { get; private set; } = [];

    public static ImportSourceFile Create(Guid tenantId, Guid importJobId, string fileName, string contentType, byte[] content) =>
        new(Guid.CreateVersion7(), tenantId)
        {
            ImportJobId = importJobId,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = content.LongLength,
            Content = content,
        };
}

public static class DataTransferDomainErrors
{
    public const string JobNotEditable = "datatransfer.job_not_editable";
    public const string JobNotRunnable = "datatransfer.job_not_runnable";
}

public static class ImportJobLimits
{
    public const int TemplateCodeMaxLength = 100;
    public const int FileNameMaxLength = 255;
    public const int ContentTypeMaxLength = 150;
    public const int CodeMaxLength = 100;
    public const int MatchKeyMaxLength = 100;
    public const int NameMaxLength = 201;
}

public interface IImportJobRepository
{
    void Add(ImportJob job, ImportSourceFile sourceFile);

    void AddRows(IEnumerable<ImportJobRow> rows);

    Task<ImportJob?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ImportSourceFile?> GetSourceFileAsync(Guid importJobId, CancellationToken cancellationToken = default);
}
