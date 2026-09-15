using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Notifications.Contracts;

/// <summary>
/// Notifications module permission definitions. Self-service keys use the "*.my.*" shape, which every base role
/// already carries through PermissionPatterns.SelfService (HR additionally has "notifications.*"), so no
/// SystemRoleDefinitions.cs change is needed.
/// </summary>
public static class NotificationsPermissions
{
    private const string Module = "notifications";
    private const string Group = "Notifications";

    /// <summary>List one's own in-app notifications and unread count.</summary>
    public static readonly Permission MyView = new(
        "notifications.my.view", Module, "View My Notifications", Group, "View the caller's own in-app notifications and notification settings");

    /// <summary>Mark one's own notifications read and change one's own notification settings.</summary>
    public static readonly Permission MyManage = new(
        "notifications.my.manage", Module, "Manage My Notifications", Group, "Mark the caller's own notifications as read and change which notifications they receive");

    public static IEnumerable<Permission> All => [MyView, MyManage];
}
