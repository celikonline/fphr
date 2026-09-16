namespace Sense.Modules.Reporting.Application;

/// <summary>Application layer error codes (localized in Web layer).</summary>
public static class ReportingErrors
{
    public const string ReportExecutionNotFound = "report_execution.not_found";
    public const string UnknownReportKey = "report_execution.unknown_report_key";
    public const string ReportPermissionDenied = "report_execution.report_permission_denied";
    public const string NotOwner = "report_execution.not_owner";
    public const string ReportNotReady = "report_execution.not_ready";
}
