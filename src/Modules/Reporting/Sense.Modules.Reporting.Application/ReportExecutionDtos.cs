using Sense.Modules.Reporting.Domain;

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

/// <summary>One report a caller may generate; display name/description are looked up client-side by Key (i18n "reports" namespace).</summary>
public sealed record ReportCatalogItemDto(string Key);

public sealed record ReportExecutionFileDto(string FileName, string ContentType, byte[] Content);
