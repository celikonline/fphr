using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Announcements.Contracts;

public static class AnnouncementsPermissions
{
    public const string View = "announcements.view";
    public const string Manage = "announcements.manage";

    public static readonly IReadOnlyList<Permission> All =
    [
        new Permission(View, "View announcements"),
        new Permission(Manage, "Create and edit announcements"),
    ];
}
