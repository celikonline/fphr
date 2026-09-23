namespace Sense.Modules.Notifications.Contracts;

/// <summary>
/// Template keys (11.3.13 catalog). Display names are web resource keys "notifications:template.{key}.name"; template
/// files are "{key}.{channel}.{lang}.sbn" in Notifications.Infrastructure.
/// </summary>
public static class NotificationTemplateKeys
{
    /// <summary>Requester: "your request was created" (Workflow.ApprovalRequested).</summary>
    public const string RequestCreated = "request.created";

    /// <summary>Approver (or fallback role members): "an approval is waiting for you" (Workflow.ApprovalTaskAssigned).</summary>
    public const string ApprovalTaskAssigned = "approval.task_assigned";

    /// <summary>Requester: "your request was approved" (Workflow.ApprovalCompleted).</summary>
    public const string RequestApproved = "request.approved";

    /// <summary>Requester: "your request was rejected" (Workflow.ApprovalRejected).</summary>
    public const string RequestRejected = "request.rejected";

    /// <summary>Holders of documents.requirements.view: "N documents expiring soon / M already expired" (Documents.DocumentExpiryReminderDue). In-app only.</summary>
    public const string DocumentExpiryReminder = "documents.expiry_reminder";

    /// <summary>Holders of ohs.incidents.manage: work accidents whose SGK notification deadline is near or missed (OccupationalSafety.SgkNotificationReminderDue).</summary>
    public const string OhsSgkNotificationDue = "ohs.sgk_notification_due";

    /// <summary>HR (employee.employees.update holders) + the new employee's manager: "a new employee starts" (Employee.EmployeeHired).</summary>
    public const string EmployeeHired = "employee.hired";

    /// <summary>The new employee: welcome e-mail (Employee.EmployeeHired). E-mail only.</summary>
    public const string EmployeeWelcome = "employee.welcome";

    /// <summary>HR + the leaving employee's manager: "an employee left" (Employee.EmployeeTerminated).</summary>
    public const string EmployeeTerminated = "employee.terminated";

    /// <summary>The leaving employee's personal e-mail: çalışma belgesi PDF attached (Employee.EmployeeTerminated). E-mail only, mandatory.</summary>
    public const string EmployeeWorkCertificate = "employee.work_certificate";

    /// <summary>The employee on their birthday (Employee.EmployeeBirthdaysToday).</summary>
    public const string EmployeeBirthday = "employee.birthday";

    /// <summary>Every active employee: a new announcement (Announcements.AnnouncementPublished with "notify employees").</summary>
    public const string AnnouncementPublished = "announcement.published";

    /// <summary>The employee: ücret pusulası PDF attached (Payroll.PayrollPeriodPaid).</summary>
    public const string PayslipReady = "payroll.payslip_ready";

    /// <summary>Members of the task's responsible role (hr/it/manager/payroll): a checklist task was assigned to them (Onboarding.OnboardingTaskAssigned).</summary>
    public const string OnboardingTaskAssigned = "onboarding.task_assigned";

    /// <summary>Members of the task's responsible role: a checklist task was completed (Onboarding.OnboardingTaskCompleted).</summary>
    public const string OnboardingTaskCompleted = "onboarding.task_completed";
}

/// <summary>Delivery channels. Push (Expo/Web Push) is a later phase.</summary>
public static class NotificationChannels
{
    public const string Email = "email";
    public const string InApp = "in_app";

    public static IReadOnlyList<string> All { get; } = [Email, InApp];
}

/// <summary>SignalR client method names on /hubs/notifications for this module.</summary>
public static class NotificationsHubMethods
{
    /// <summary>Payload: the new in-app notification (same shape as the list endpoint's items).</summary>
    public const string UserNotificationReceived = "ReceiveUserNotification";
}
