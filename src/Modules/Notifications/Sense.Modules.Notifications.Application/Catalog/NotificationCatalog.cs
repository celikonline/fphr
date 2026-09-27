using Sense.Modules.Notifications.Contracts;

namespace Sense.Modules.Notifications.Application.Catalog;

public enum NotificationCategory
{
    Account,
    Request,
    Approval,

    /// <summary>Periodic digest-style reminders (e.g. documents nearing/past expiry), not tied to a single request.</summary>
    Reminder,

    /// <summary>Employee lifecycle and personal occasions: hire, exit, welcome, work certificate, birthday.</summary>
    Employee,

    /// <summary>Company announcements.</summary>
    Announcement,

    /// <summary>Payroll documents (ücret pusulası).</summary>
    Payroll,

    /// <summary>Onboarding/offboarding checklist tasks assigned to a responsible role (hr/it/manager/payroll).</summary>
    Onboarding,
}

/// <summary>
/// One template in the catalog (11.3.13). <see cref="DefaultChannels"/> decide delivery when the user has no
/// preference row; <see cref="IsMandatory"/> templates are always delivered on their default channels and cannot be
/// turned off.
/// </summary>
public sealed record NotificationTemplateDefinition(
    string Key, NotificationCategory Category, IReadOnlyList<string> DefaultChannels, bool IsMandatory)
{
    public bool IsUserConfigurable => !IsMandatory;

    public bool SupportsChannel(string channel) => DefaultChannels.Contains(channel, StringComparer.Ordinal);
}

/// <summary>
/// Templates this module delivers today. Other modules' keys (leave.*, shift.*, identity.* ...) join here as their
/// events are consumed; identity invitation/password reset mails are still sent by Identity itself.
/// </summary>
public static class NotificationCatalog
{
    private static readonly string[] EmailAndInApp = [NotificationChannels.Email, NotificationChannels.InApp];
    private static readonly string[] InAppOnly = [NotificationChannels.InApp];
    private static readonly string[] EmailOnly = [NotificationChannels.Email];

    // Engagement/time-sensitive templates that also fan out over Expo push (push-bildirimleri-tasarim.md §3.1).
    // Mandatory/statutory reminders (OhsSgkNotificationDue, EmployeeWorkCertificate) are deliberately excluded.
    private static readonly string[] EmailInAppAndPush = [NotificationChannels.Email, NotificationChannels.InApp, NotificationChannels.Push];

    public static IReadOnlyList<NotificationTemplateDefinition> All { get; } =
    [
        new(NotificationTemplateKeys.RequestCreated, NotificationCategory.Request, EmailAndInApp, IsMandatory: false),
        new(NotificationTemplateKeys.RequestApproved, NotificationCategory.Request, EmailInAppAndPush, IsMandatory: false),
        new(NotificationTemplateKeys.RequestRejected, NotificationCategory.Request, EmailInAppAndPush, IsMandatory: false),
        new(NotificationTemplateKeys.ApprovalTaskAssigned, NotificationCategory.Approval, EmailInAppAndPush, IsMandatory: false),
        new(NotificationTemplateKeys.LeaveAddedOnBehalf, NotificationCategory.Request, EmailAndInApp, IsMandatory: false),
        new(NotificationTemplateKeys.DocumentExpiryReminder, NotificationCategory.Reminder, InAppOnly, IsMandatory: false),
        // Missing the statutory SGK deadline is a legal breach, so this reminder cannot be switched off.
        new(NotificationTemplateKeys.OhsSgkNotificationDue, NotificationCategory.Reminder, InAppOnly, IsMandatory: true),
        new(NotificationTemplateKeys.EmployeeHired, NotificationCategory.Employee, EmailAndInApp, IsMandatory: false),
        new(NotificationTemplateKeys.EmployeeWelcome, NotificationCategory.Employee, EmailOnly, IsMandatory: false),
        new(NotificationTemplateKeys.EmployeeTerminated, NotificationCategory.Employee, EmailAndInApp, IsMandatory: false),
        // 4857 md.28: the employer must hand out the çalışma belgesi, and the leaver has no account left to opt out with.
        new(NotificationTemplateKeys.EmployeeWorkCertificate, NotificationCategory.Employee, EmailOnly, IsMandatory: true),
        new(NotificationTemplateKeys.EmployeeBirthday, NotificationCategory.Employee, EmailInAppAndPush, IsMandatory: false),
        new(NotificationTemplateKeys.AnnouncementPublished, NotificationCategory.Announcement, EmailInAppAndPush, IsMandatory: false),
        new(NotificationTemplateKeys.PayslipReady, NotificationCategory.Payroll, EmailInAppAndPush, IsMandatory: false),
        new(NotificationTemplateKeys.OnboardingTaskAssigned, NotificationCategory.Onboarding, InAppOnly, IsMandatory: false),
        new(NotificationTemplateKeys.OnboardingTaskCompleted, NotificationCategory.Onboarding, InAppOnly, IsMandatory: false),
    ];

    public static NotificationTemplateDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>Effective on/off for a user: mandatory → on; explicit preference wins; otherwise the channel default.</summary>
    public static bool IsEnabled(NotificationTemplateDefinition definition, string channel, bool? preference)
    {
        if (!definition.SupportsChannel(channel))
        {
            return false;
        }

        return definition.IsMandatory || (preference ?? true);
    }
}
