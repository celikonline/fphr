using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Sense.Modules.DataTransfer.Contracts;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.DataTransfer.Application.ImportJobs;

/// <summary>Uploads a file for a template: stores it, detects the key row, suggests the column mapping (status Uploaded).</summary>
[RequiresPermission(DataTransferPermissions.ImportRunKey)]
public sealed record CreateImportJobCommand(string TemplateCode, string FileName, byte[] Content) : ICommand<ImportJobDto>;

public sealed class CreateImportJobValidator : AbstractValidator<CreateImportJobCommand>
{
    public CreateImportJobValidator()
    {
        RuleFor(x => x.TemplateCode).NotEmpty().MaximumLength(ImportJobLimits.TemplateCodeMaxLength);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(ImportJobLimits.FileNameMaxLength);
        RuleFor(x => x.Content).NotNull();
    }
}

public sealed class CreateImportJobHandler(
    ITenantContext tenant,
    ICurrentUser currentUser,
    ImportTemplateCatalog catalog,
    ISpreadsheetReader reader,
    IImportJobRepository jobs,
    TimeProvider clock) : ICommandHandler<CreateImportJobCommand, ImportJobDto>
{
    private const string XlsxExtension = ".xlsx";
    private const string CsvExtension = ".csv";
    private const string CsvContentType = "text/csv";
    private const string MaxArg = "max";

    public async Task<Result<ImportJobDto>> Handle(CreateImportJobCommand command, CancellationToken cancellationToken)
    {
        var found = catalog.Find(command.TemplateCode);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var template = found.Value;
        var authorized = await catalog.AuthorizeAsync(template, cancellationToken);
        if (authorized.IsFailure)
        {
            return authorized.Error;
        }

        var fileName = Path.GetFileName(command.FileName.Trim());
        var extension = Path.GetExtension(fileName);
        ImportFileFormat format;
        if (string.Equals(extension, XlsxExtension, StringComparison.OrdinalIgnoreCase))
        {
            format = ImportFileFormat.Xlsx;
        }
        else if (string.Equals(extension, CsvExtension, StringComparison.OrdinalIgnoreCase))
        {
            format = ImportFileFormat.Csv;
        }
        else
        {
            return Error.Rule(DataTransferErrors.FileTypeNotSupported);
        }

        if (command.Content.Length == 0)
        {
            return Error.Validation(DataTransferErrors.FileRequired);
        }

        if (command.Content.LongLength > ImportLimits.MaxFileSizeBytes)
        {
            return Error.Rule(DataTransferErrors.FileTooLarge, (MaxArg, "10 MB"));
        }

        var read = reader.Read(command.Content, format, ImportLimits.MaxRows + 2);
        if (read.IsFailure)
        {
            return read.Error;
        }

        var sheet = read.Value;
        var columns = await catalog.GetColumnsAsync(template, ImportCultures.Resolve(null), cancellationToken);
        var hasKeyRow = ColumnAutoMapper.IsKeyRow(sheet.Rows.FirstOrDefault(), columns.Select(c => c.Column.Key));
        var dataRowCount = ColumnAutoMapper.DataRows(sheet, hasKeyRow).Count;
        if (dataRowCount > ImportLimits.MaxRows)
        {
            return Error.Rule(DataTransferErrors.TooManyRows, (MaxArg, ImportLimits.MaxRows.ToString("N0", CultureInfo.CurrentCulture)));
        }

        if (dataRowCount == 0 || sheet.Headers.All(string.IsNullOrWhiteSpace))
        {
            return Error.Rule(DataTransferErrors.FileEmpty);
        }

        var sourceColumns = ColumnAutoMapper.BuildSourceColumns(sheet, hasKeyRow, columns);
        var job = ImportJob.Create(
            tenant.TenantId, template.Code, fileName, format, hasKeyRow, dataRowCount, sourceColumns, currentUser.UserId, currentUser.DisplayName);
        var contentType = format == ImportFileFormat.Xlsx ? FileDownload.XlsxContentType : CsvContentType;
        jobs.Add(job, ImportSourceFile.Create(tenant.TenantId, job.Id, fileName, contentType, command.Content));

        // CreatedAt is stamped on save; report the upload time meanwhile.
        return ImportJobDto.From(job) with { CreatedAt = clock.GetUtcNow().UtcDateTime };
    }
}

/// <summary>Saves the column mapping and run options (status Mapped). Missing required/match-key columns → 422 IMPORT_UNMAPPED_REQUIRED.</summary>
[RequiresPermission(DataTransferPermissions.ImportRunKey)]
public sealed record SaveImportJobMappingCommand(Guid Id, IReadOnlyList<ColumnMappingDto> Mapping, string Mode, string FailurePolicy) : ICommand<ImportJobDto>;

public sealed class SaveImportJobMappingValidator : AbstractValidator<SaveImportJobMappingCommand>
{
    public SaveImportJobMappingValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Mapping).NotNull();
        RuleFor(x => x.Mode).Must(m => Enum.TryParse<ImportJobMode>(m, ignoreCase: true, out var v) && Enum.IsDefined(v)).WithMessage(DataTransferErrors.ModeInvalid);
        RuleFor(x => x.FailurePolicy).Must(p => Enum.TryParse<ImportJobFailurePolicy>(p, ignoreCase: true, out var v) && Enum.IsDefined(v)).WithMessage(DataTransferErrors.FailurePolicyInvalid);
    }
}

public sealed class SaveImportJobMappingHandler(IImportJobRepository jobs, ImportTemplateCatalog catalog) : ICommandHandler<SaveImportJobMappingCommand, ImportJobDto>
{
    public async Task<Result<ImportJobDto>> Handle(SaveImportJobMappingCommand command, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.Id, cancellationToken);
        if (job is null)
        {
            return Error.NotFound(DataTransferErrors.JobNotFound);
        }

        var editable = job.EnsureEditable();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        var found = catalog.Find(job.TemplateCode);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var template = found.Value;
        var authorized = await catalog.AuthorizeAsync(template, cancellationToken);
        if (authorized.IsFailure)
        {
            return authorized.Error;
        }

        var mode = Enum.Parse<ImportJobMode>(command.Mode, ignoreCase: true);
        if (!template.SupportedModes.Any(m => m.ToString() == mode.ToString()))
        {
            return Error.Validation(DataTransferErrors.ModeNotSupported);
        }

        var columns = await template.GetColumnsAsync(cancellationToken);
        var mapping = ImportMappingRules.Normalize(job, command.Mapping, columns);
        if (mapping.IsFailure)
        {
            return mapping.Error;
        }

        var unmapped = ImportMappingRules.MissingBlocking(mapping.Value, columns);
        if (unmapped is not null)
        {
            return unmapped;
        }

        var set = job.SetMapping(mapping.Value, mode, Enum.Parse<ImportJobFailurePolicy>(command.FailurePolicy, ignoreCase: true));
        return set.IsFailure ? set.Error : ImportJobDto.From(job);
    }
}

public static class ImportMappingRules
{
    /// <summary>One entry per source column of the job; unknown source indexes/target keys or a target used twice are invalid.</summary>
    public static Result<IReadOnlyList<ImportColumnMapping>> Normalize(ImportJob job, IReadOnlyList<ColumnMappingDto> requested, IReadOnlyList<ImportColumn> columns)
    {
        var sourceIndexes = job.SourceColumns.Select(c => c.Index).ToHashSet();
        var keys = columns.ToDictionary(c => c.Key, c => c.Key, StringComparer.OrdinalIgnoreCase);
        var byIndex = new Dictionary<int, string?>();
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in requested)
        {
            if (!sourceIndexes.Contains(entry.SourceIndex) || byIndex.ContainsKey(entry.SourceIndex))
            {
                return Error.Validation(DataTransferErrors.MappingInvalid);
            }

            string? target = null;
            if (!string.IsNullOrWhiteSpace(entry.TargetKey))
            {
                if (!keys.TryGetValue(entry.TargetKey.Trim(), out target) || !used.Add(target))
                {
                    return Error.Validation(DataTransferErrors.MappingInvalid);
                }
            }

            byIndex[entry.SourceIndex] = target;
        }

        return Result.Success<IReadOnlyList<ImportColumnMapping>>(
            job.SourceColumns.Select(c => new ImportColumnMapping(c.Index, byIndex.GetValueOrDefault(c.Index))).ToList());
    }

    /// <summary>422 IMPORT_UNMAPPED_REQUIRED with args.missing = comma-separated template column keys, or null when complete.</summary>
    public static Error? MissingBlocking(IReadOnlyList<ImportColumnMapping> mapping, IReadOnlyList<ImportColumn> columns)
    {
        var mapped = mapping.Where(m => m.TargetKey is not null).Select(m => m.TargetKey!).ToHashSet(StringComparer.Ordinal);
        var missing = columns.Where(c => c.IsBlocking && !mapped.Contains(c.Key)).Select(c => c.Key).ToList();
        return missing.Count == 0 ? null : Error.Rule(ImportErrorCodes.UnmappedRequired, (ImportErrorCodes.MissingArg, string.Join(',', missing)));
    }
}

/// <summary>Runs a mapped job synchronously: rows go to the template's module handler, outcomes are recorded here.</summary>
[RequiresPermission(DataTransferPermissions.ImportRunKey)]
public sealed record RunImportJobCommand(Guid Id) : ICommand<ImportJobDto>;

public sealed class RunImportJobHandler(
    IImportJobRepository jobs,
    ImportTemplateCatalog catalog,
    ISpreadsheetReader reader,
    TimeProvider clock,
    ILogger<RunImportJobHandler> logger) : ICommandHandler<RunImportJobCommand, ImportJobDto>
{
    public async Task<Result<ImportJobDto>> Handle(RunImportJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.Id, cancellationToken);
        if (job is null)
        {
            return Error.NotFound(DataTransferErrors.JobNotFound);
        }

        var runnable = job.EnsureRunnable();
        if (runnable.IsFailure)
        {
            return runnable.Error;
        }

        var template = catalog.Find(job.TemplateCode);
        var handler = catalog.FindHandler(job.TemplateCode);
        if (template.IsFailure || handler.IsFailure)
        {
            return Error.NotFound(DataTransferErrors.TemplateNotFound);
        }

        var authorized = await catalog.AuthorizeAsync(template.Value, cancellationToken);
        if (authorized.IsFailure)
        {
            return authorized.Error;
        }

        // Org levels may have changed since mapping: drop targets that no longer exist and re-check required columns.
        var columns = await template.Value.GetColumnsAsync(cancellationToken);
        var keys = columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var mapping = job.Mapping.Where(m => m.TargetKey is not null && keys.Contains(m.TargetKey)).ToList();
        if (ImportMappingRules.MissingBlocking(mapping, columns) is { } unmapped)
        {
            return unmapped;
        }

        var file = await jobs.GetSourceFileAsync(job.Id, cancellationToken);
        if (file is null)
        {
            return Error.NotFound(DataTransferErrors.JobNotFound);
        }

        var read = reader.Read(file.Content, job.Format, ImportLimits.MaxRows + 2);
        if (read.IsFailure)
        {
            return read.Error;
        }

        var rows = ColumnAutoMapper.DataRows(read.Value, job.HasKeyRow)
            .Select(r => new ImportRow(r.RowNumber, mapping.ToDictionary(m => m.TargetKey!, m => r.Cell(m.SourceIndex), StringComparer.Ordinal)))
            .ToList();
        var batch = new ImportBatch(
            Enum.Parse<ImportMode>(job.Mode.ToString()),
            Enum.Parse<ImportFailurePolicy>(job.FailurePolicy.ToString()),
            mapping.Select(m => m.TargetKey!).ToList(),
            rows);

        job.Start(clock.GetUtcNow().UtcDateTime);
        ImportBatchResult result;
        try
        {
            result = await handler.Value.ProcessAsync(batch, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing was saved by the module handler (single save at the end); record the failed run instead of losing the job.
            logger.LogError(ex, "Import job {ImportJobId} ({TemplateCode}) failed", job.Id, job.TemplateCode);
            job.Fail(DataTransferErrors.RunFailed, clock.GetUtcNow().UtcDateTime);
            return ImportJobDto.From(job);
        }

        jobs.AddRows(result.Rows.Select(r => ImportJobRow.Create(
            job.TenantId,
            job.Id,
            r.RowNumber,
            Enum.Parse<ImportJobRowOutcome>(r.Outcome.ToString()),
            Truncate(r.MatchKey, ImportJobLimits.MatchKeyMaxLength),
            r.TargetEntityId,
            r.Errors.Select(e => new ImportJobRowError(e.Column, e.Code, e.Args)).ToList())));

        var counts = new ImportJobCounts(
            result.Rows.Count(r => r.Outcome == ImportRowOutcome.Inserted),
            result.Rows.Count(r => r.Outcome == ImportRowOutcome.Updated),
            result.Rows.Count(r => r.Outcome == ImportRowOutcome.Skipped),
            result.Rows.Count(r => r.Outcome == ImportRowOutcome.Error));
        job.Finish(counts, result.Applied, clock.GetUtcNow().UtcDateTime);
        return ImportJobDto.From(job);
    }

    private static string? Truncate(string? value, int max) => value is { Length: var length } && length > max ? value[..max] : value;
}
