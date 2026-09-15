using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Notifications.Contracts;

/// <summary>
/// Notifications permissions. All keys are self-service ("*.self.*"), which every system role already carries through
/// SystemRoleDefinitions.PermissionPatterns.SelfService - no role list change needed.
/// </summary>
public static class NotificationsPermissions
{
    private const string Module = "notifications";
    private const string Group = "Notifications";

    public const string SelfViewKey = "notifications.self.view";
    public const string SelfPreferencesKey = "notifications.self.preferences";

    /// <summary>List own in-app notifications, unread count, mark as read.</summary>
    public static readonly Permission SelfView = new(
        SelfViewKey, Module, "View My Notifications", Group, "List the caller's own notifications and mark them as read");

    /// <summary>Read and change own notification channel preferences.</summary>
    public static readonly Permission SelfPreferences = new(
        SelfPreferencesKey, Module, "Manage My Notification Preferences", Group, "Choose which notifications the caller receives by e-mail or in-app");

    public static IEnumerable<Permission> All => [SelfView, SelfPreferences];
}
