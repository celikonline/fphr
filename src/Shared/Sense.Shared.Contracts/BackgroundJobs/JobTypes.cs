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

    /// <summary>Faz 4: remind about documents expiring soon or already expired (Documents module).</summary>
    DocumentExpiryReminder = 7,

    /// <summary>Faz 9: KVKK retention sweep - anonymize candidates whose retention window elapsed (Recruitment module).</summary>
    CandidateRetentionSweep = 8,

    /// <summary>Faz 6: Automatically lock shift periods that are due (Attendance module).</summary>
    ShiftPeriodAutoLock = 9,

    /// <summary>İSG: remind about work accidents whose SGK notification deadline is near or missed (OccupationalSafety module).</summary>
    OhsSgkNotificationReminder = 10,

    /// <summary>Daily birthday greetings: publishes the employees whose birthday is today (Employee module).</summary>
    EmployeeBirthdayGreeting = 11,

    /// <summary>Payroll (E2): remind employees whose paid payslip is still unacknowledged. Handler + seed are added by the Payroll lane.</summary>
    PayslipAcknowledgementReminder = 12,

    /// <summary>Attendance (Zaman &amp; Devam): remind employees/managers about days with a missing or incalculable punch. Handler + seed are added by the Attendance lane.</summary>
    AttendanceMissingPunchReminder = 13,

    /// <summary>DataTransfer (Z5.4, T2): delete uploaded import files of templates with sensitive columns (card/PIN) past their TTL or once the job has finished.</summary>
    ImportSensitiveSourceCleanup = 14,
}
