namespace Sense.Modules.Reporting.Application;

/// <summary>Application layer error codes (localized in SharedResource{,.en}.resx).</summary>
public static class ReportingErrors
{
    public const string ReportExecutionNotFound = "report_execution.not_found";
    public const string UnknownReportKey = "report_execution.unknown_report_key";
    public const string ReportPermissionDenied = "report_execution.report_permission_denied";
    public const string NotOwner = "report_execution.not_owner";
    public const string ReportNotReady = "report_execution.not_ready";

    /// <summary>A parameter the provider marks <c>IsRequired</c> is missing/blank.</summary>
    public const string ParameterRequired = "report_execution.parameter_required";

    /// <summary>A parameter is present but malformed for its kind (non-GUID unit id, month outside 1–12, bad date, from &gt; to…).</summary>
    public const string ParameterInvalid = "report_execution.parameter_invalid";

    /// <summary>The execution's file cannot be shown as a grid (not an .xlsx, or unreadable); the web offers the download only.</summary>
    public const string PreviewNotAvailable = "report_execution.preview_not_available";

    /// <summary>The requested sheet index does not exist in the workbook.</summary>
    public const string SheetInvalid = "report_execution.sheet_invalid";

    /// <summary>The requested sort column index is not a column of the sheet.</summary>
    public const string SortColumnInvalid = "report_execution.sort_column_invalid";
}
