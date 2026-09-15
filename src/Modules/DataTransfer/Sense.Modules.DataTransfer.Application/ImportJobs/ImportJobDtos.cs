using Sense.Modules.DataTransfer.Domain.ImportJobs;

namespace Sense.Modules.DataTransfer.Application.ImportJobs;

// Enum-like values are plain strings ("Upsert", "CompletedWithErrors" …): the global JSON enum converter would camelCase them.

/// <summary>Import template card (web/src/services/data-transfer.service.ts ImportTemplate).</summary>
public sealed record ImportTemplateDto(string Code, string Name, string Description, IReadOnlyList<string> SupportedModes, IReadOnlyList<ImportColumnDto> Columns);

public sealed record ImportColumnDto(string Key, string Header, string DataType, string Requirement, bool IsMatchKey, IReadOnlyList<string>? Options);

public sealed record SourceColumnDto(int Index, string Header, IReadOnlyList<string> Samples, string? SuggestedTargetKey, bool AutoMatched);

public sealed record ColumnMappingDto(int SourceIndex, string? TargetKey);

public sealed record ImportJobDto(
    Guid Id,
    string TemplateCode,
    string FileName,
    string Status,
    string Mode,
    string FailurePolicy,
    int TotalRows,
    int InsertedCount,
    int UpdatedCount,
    int SkippedCount,
    int ErrorCount,
    IReadOnlyList<SourceColumnDto> SourceColumns,
    IReadOnlyList<ColumnMappingDto> Mapping,
    DateTime CreatedAt,
    DateTime? FinishedAt)
{
    public static ImportJobDto From(ImportJob job) => new(
        job.Id,
        job.TemplateCode,
        job.FileName,
        job.Status.ToString(),
        job.Mode.ToString(),
        job.FailurePolicy.ToString(),
        job.TotalRows,
        job.InsertedCount,
        job.UpdatedCount,
        job.SkippedCount,
        job.ErrorCount,
        job.SourceColumns.Select(c => new SourceColumnDto(c.Index, c.Header, c.Samples, c.SuggestedTargetKey, c.AutoMatched)).ToList(),
        job.Mapping.Select(m => new ColumnMappingDto(m.SourceIndex, m.TargetKey)).ToList(),
        job.CreatedAt,
        job.FinishedAt);
}

public sealed record ImportJobListItemDto(
    Guid Id,
    string TemplateCode,
    string FileName,
    string Status,
    string Mode,
    int TotalRows,
    int InsertedCount,
    int UpdatedCount,
    int SkippedCount,
    int ErrorCount,
    DateTime CreatedAt,
    string? RequestedByName)
{
    public static ImportJobListItemDto From(ImportJob job) => new(
        job.Id, job.TemplateCode, job.FileName, job.Status.ToString(), job.Mode.ToString(), job.TotalRows,
        job.InsertedCount, job.UpdatedCount, job.SkippedCount, job.ErrorCount, job.CreatedAt, job.RequestedByName);
}

public sealed record RowErrorDto(string? Column, string Code, string Message);

public sealed record ImportJobRowDto(int RowNumber, string Outcome, string? MatchKey, IReadOnlyList<RowErrorDto> Errors);
