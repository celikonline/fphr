using Sense.Modules.Reporting.Contracts;
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

/// <summary>
/// One report the caller may generate (Kolay İK "rapor türü"): the stable key (web resolves the display name/description
/// from the "reports" i18n namespace), its menu category (<see cref="ReportCategories"/>) and the filters it accepts -
/// the web draws the parameter form from <see cref="Parameters"/> (birim ağacı, yıl, ay, tarih aralığı, anahtar).
/// </summary>
public sealed record ReportCatalogItemDto(string Key, string CategoryKey, IReadOnlyList<ReportParameterDescriptor> Parameters);

public sealed record ReportExecutionFileDto(string FileName, string ContentType, byte[] Content);
