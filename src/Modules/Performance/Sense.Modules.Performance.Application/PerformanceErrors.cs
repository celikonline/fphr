namespace Sense.Modules.Performance.Application;

/// <summary>Application layer error codes (cross-module checks the Domain can't make) - localized in the Web layer.</summary>
public static class PerformanceErrors
{
    public const string EvaluatorNotActive = "performance.participant.evaluator_not_active";

    /// <summary>GoalEntryBy=Employee'de yalnız çalışanın kendisi, FirstManager'da yalnız yöneticisi hedef/sonuç girebilir.</summary>
    public const string GoalEntryModeMismatch = "performance.goal.entry_mode_mismatch";

    public const string ReportNotReady = "performance.report.not_ready";
    public const string ReportSelfDownloadDisabled = "performance.report.self_download_disabled";
}
