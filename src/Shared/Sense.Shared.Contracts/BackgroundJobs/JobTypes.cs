namespace Sense.Shared.Contracts.BackgroundJobs;

/// <summary>
/// Enumeration of background job types.
/// </summary>
public enum JobType
{
    /// <summary>Send email notification.</summary>
    SendEmail = 1,

    /// <summary>Generate and export report.</summary>
    ExportReport = 2,

    /// <summary>Synchronize attendance data.</summary>
    SyncAttendance = 3,

    /// <summary>Send approval reminder.</summary>
    ApprovalReminder = 4,

    /// <summary>Execute payroll run.</summary>
    PayrollRun = 5,

    /// <summary>Generate daily audit summary.</summary>
    DailyAuditSummary = 6,
}
