using System.Globalization;
using FluentValidation;
using Sense.Modules.DataTransfer.Contracts;
using Sense.Modules.DataTransfer.Domain.ImportJobs;
using Sense.Shared.Contracts.Import;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.DataTransfer.Application.ImportJobs;

/// <summary>Registered import templates with localized headers (dynamic columns resolved for the current tenant).</summary>
[RequiresPermission(DataTransferPermissions.ImportViewKey)]
public sealed record ListImportTemplatesQuery(string? Lang) : IQuery<IReadOnlyList<ImportTemplateDto>>;

public sealed class ListImportTemplatesHandler(ImportTemplateCatalog catalog) : IQueryHandler<ListImportTemplatesQuery, IReadOnlyList<ImportTemplateDto>>
{
    public async Task<Result<IReadOnlyList<ImportTemplateDto>>> Handle(ListImportTemplatesQuery query, CancellationToken cancellationToken)
    {
        var culture = ImportCultures.Resolve(query.Lang);
        var result = new List<ImportTemplateDto>();
        foreach (var template in catalog.All)
        {
            var columns = await catalog.GetColumnsAsync(template, culture, cancellationToken);
            result.Add(catalog.ToDto(template, columns, culture));
        }

        return result;
    }
}

/// <summary>Template workbook: headers (row 1), hidden key row (row 2), lookup dropdowns, instructions and examples sheets.</summary>
[RequiresPermission(DataTransferPermissions.TemplateDownloadKey)]
public sealed record DownloadImportTemplateQuery(string Code, string? Lang) : IQuery<FileDownload>;

public sealed class DownloadImportTemplateHandler(ImportTemplateCatalog catalog, IImportWorkbookWriter writer) : IQueryHandler<DownloadImportTemplateQuery, FileDownload>
{
    private const string InstructionPrefix = "import.instructions.";
    private const string LabelPrefix = "import.template.label.";
    private const string MaxRowsArg = "max";

    public static readonly IReadOnlyList<string> GenericInstructionKeys =
    [
        InstructionPrefix + "headers",
        InstructionPrefix + "columns",
        InstructionPrefix + "dates",
        InstructionPrefix + "lookups",
        InstructionPrefix + "update_only",
        InstructionPrefix + "empty_cells",
        InstructionPrefix + "examples",
        InstructionPrefix + "limits",
    ];

    public async Task<Result<FileDownload>> Handle(DownloadImportTemplateQuery query, CancellationToken cancellationToken)
    {
        var found = catalog.Find(query.Code);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var template = found.Value;
        var culture = ImportCultures.Resolve(query.Lang);
        var columns = await catalog.GetColumnsAsync(template, culture, cancellationToken);
        var args = new Dictionary<string, string> { [MaxRowsArg] = ImportLimits.MaxRows.ToString("N0", culture) };

        string Label(string name) => catalog.Text(LabelPrefix + name, culture);

        var model = new TemplateWorkbookModel(
            catalog.Text(template.NameKey, culture),
            columns.Select(c => new TemplateWorkbookColumn(
                c.Column,
                c.Header,
                c.Column.DescriptionKey is { } descriptionKey ? catalog.Text(descriptionKey, culture) : null,
                c.Column.IsMatchKey ? Label("match_key")
                    : c.Column.Requirement == ImportColumnRequirement.Always ? Label("required")
                    : c.Column.Requirement == ImportColumnRequirement.OnInsert ? Label("required_on_insert")
                    : Label("optional"))).ToList(),
            [.. GenericInstructionKeys.Select(k => catalog.Text(k, culture, args)), .. template.InstructionKeys.Select(k => catalog.Text(k, culture))],
            template.GetSampleRows(columns.Select(c => c.Column).ToList())
                .Select(s => new TemplateWorkbookSample(s.IsValid, s.Values, catalog.Text(s.NoteKey, culture))).ToList(),
            new TemplateWorkbookLabels(
                Label("instructions_sheet"),
                Label("examples_sheet"),
                Label("lists_sheet"),
                Label("instructions_title"),
                Label("columns_title"),
                Label("column"),
                Label("requirement"),
                Label("description"),
                Label("example_note")));

        return new FileDownload(template.Code + ".xlsx", FileDownload.XlsxContentType, writer.BuildTemplate(model));
    }
}

/// <summary>Import history (newest first).</summary>
[RequiresPermission(DataTransferPermissions.ImportViewKey)]
public sealed record ListImportJobsQuery(PagedQuery Paging) : IQuery<PagedResult<ImportJobListItemDto>>;

public sealed class ListImportJobsHandler(IImportJobReadStore store) : IQueryHandler<ListImportJobsQuery, PagedResult<ImportJobListItemDto>>
{
    public async Task<Result<PagedResult<ImportJobListItemDto>>> Handle(ListImportJobsQuery query, CancellationToken cancellationToken)
    {
        var page = await store.ListAsync(query.Paging, cancellationToken);
        return page.Map(ImportJobListItemDto.From);
    }
}

[RequiresPermission(DataTransferPermissions.ImportViewKey)]
public sealed record GetImportJobQuery(Guid Id) : IQuery<ImportJobDto>;

public sealed class GetImportJobHandler(IImportJobRepository jobs) : IQueryHandler<GetImportJobQuery, ImportJobDto>
{
    public async Task<Result<ImportJobDto>> Handle(GetImportJobQuery query, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(query.Id, cancellationToken);
        return job is null ? Error.NotFound(DataTransferErrors.JobNotFound) : ImportJobDto.From(job);
    }
}

/// <summary>Row outcomes of a job (?outcome=Inserted|Updated|Skipped|Error), messages localized to the request culture.</summary>
[RequiresPermission(DataTransferPermissions.ImportViewKey)]
public sealed record ListImportJobRowsQuery(Guid Id, string? Outcome, PagedQuery Paging) : IQuery<PagedResult<ImportJobRowDto>>;

public sealed class ListImportJobRowsValidator : AbstractValidator<ListImportJobRowsQuery>
{
    public ListImportJobRowsValidator() =>
        RuleFor(x => x.Outcome)
            .Must(o => string.IsNullOrWhiteSpace(o) || (Enum.TryParse<ImportJobRowOutcome>(o, ignoreCase: true, out var v) && Enum.IsDefined(v)))
            .WithMessage(DataTransferErrors.OutcomeInvalid);
}

public sealed class ListImportJobRowsHandler(IImportJobRepository jobs, IImportJobReadStore store, ImportTemplateCatalog catalog)
    : IQueryHandler<ListImportJobRowsQuery, PagedResult<ImportJobRowDto>>
{
    public async Task<Result<PagedResult<ImportJobRowDto>>> Handle(ListImportJobRowsQuery query, CancellationToken cancellationToken)
    {
        if (await jobs.GetAsync(query.Id, cancellationToken) is null)
        {
            return Error.NotFound(DataTransferErrors.JobNotFound);
        }

        ImportJobRowOutcome? outcome = string.IsNullOrWhiteSpace(query.Outcome) ? null : Enum.Parse<ImportJobRowOutcome>(query.Outcome, ignoreCase: true);
        var page = await store.ListRowsAsync(query.Id, outcome, query.Paging, cancellationToken);
        var culture = ImportCultures.Resolve(null);
        return page.Map(r => new ImportJobRowDto(
            r.RowNumber,
            r.Outcome.ToString(),
            r.MatchKey,
            r.Errors.Select(e => new RowErrorDto(e.Column, e.Code, catalog.RowErrorMessage(e, culture))).ToList()));
    }
}

/// <summary>Result workbook: the uploaded rows plus localized "Sonuç" and "Hata açıklaması" columns.</summary>
[RequiresPermission(DataTransferPermissions.ResultDownloadKey)]
public sealed record DownloadImportResultQuery(Guid Id, string? Lang) : IQuery<FileDownload>;

public sealed class DownloadImportResultHandler(
    IImportJobRepository jobs,
    IImportJobReadStore store,
    ISpreadsheetReader reader,
    ImportTemplateCatalog catalog,
    IImportWorkbookWriter writer) : IQueryHandler<DownloadImportResultQuery, FileDownload>
{
    private const string ResultPrefix = "import.result.";
    private const string OutcomePrefix = "import.outcome.";
    private const string ErrorSeparator = "; ";

    public async Task<Result<FileDownload>> Handle(DownloadImportResultQuery query, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(query.Id, cancellationToken);
        if (job is null)
        {
            return Error.NotFound(DataTransferErrors.JobNotFound);
        }

        if (!job.IsFinished)
        {
            return Error.Conflict(DataTransferErrors.JobNotFinished);
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

        var sheet = read.Value;
        var culture = ImportCultures.Resolve(query.Lang);
        var outcomes = (await store.ListAllRowsAsync(job.Id, cancellationToken)).ToDictionary(r => r.RowNumber);
        var headerByKey = job.Mapping
            .Where(m => m.TargetKey is not null)
            .ToDictionary(m => m.TargetKey!, m => job.SourceColumns.FirstOrDefault(c => c.Index == m.SourceIndex)?.Header ?? m.TargetKey!, StringComparer.Ordinal);

        var rows = ColumnAutoMapper.DataRows(sheet, job.HasKeyRow).Select(r =>
        {
            if (!outcomes.TryGetValue(r.RowNumber, out var outcome))
            {
                return new ResultWorkbookRow(r.RowNumber, r.Cells, null, null, IsError: false);
            }

            var errors = outcome.Errors.Count == 0
                ? null
                : string.Join(ErrorSeparator, outcome.Errors.Select(e =>
                {
                    var message = catalog.RowErrorMessage(e, culture);
                    return e.Column is { } column ? $"{headerByKey.GetValueOrDefault(column, column)}: {message}" : message;
                }));
            return new ResultWorkbookRow(
                r.RowNumber, r.Cells, catalog.Text(OutcomePrefix + outcome.Outcome, culture), errors, outcome.Outcome == ImportJobRowOutcome.Error);
        }).ToList();

        var keyRow = job.HasKeyRow ? sheet.Rows.FirstOrDefault(r => r.RowNumber == ColumnAutoMapper.KeyRowNumber)?.Cells : null;
        var model = new ResultWorkbookModel(
            catalog.Text(ResultPrefix + "sheet", culture),
            sheet.Headers,
            keyRow,
            rows,
            catalog.Text(ResultPrefix + "outcome_header", culture),
            catalog.Text(ResultPrefix + "error_header", culture));

        var baseName = Path.GetFileNameWithoutExtension(job.FileName);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"{baseName}-{catalog.Text(ResultPrefix + "file_suffix", culture)}.xlsx");
        return new FileDownload(fileName, FileDownload.XlsxContentType, writer.BuildResult(model));
    }
}
