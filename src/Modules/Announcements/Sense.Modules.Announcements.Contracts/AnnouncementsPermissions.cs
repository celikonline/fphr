using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Announcements.Contracts;

/// <summary>
/// Announcements module permission definitions (announcements feed + Support sub-area: Help Center and "Desteğe
/// bağlan", roadmap #69/#71). Keys are exactly module.resource.action (3 segments, snake_case) per
/// Sense.Tests.Architecture.OnionArchitectureTests.PermissionKeys_FollowNamingConvention.
/// <para>
/// Role reach (Sense.Modules.Identity.Application.Roles.SystemRoleDefinitions): HR/HRSpecialist get everything via
/// "announcements.*"; Employee/Intern/Contractor already had "announcements.feed.*". Help Center and support tickets
/// must be open to every signed-in user (11.3.13): "announcements.my.tickets" follows the repo's self-service key
/// shape (expense.my.claims, assets.my.assets) so the "*.my.*" SelfService wildcard reaches most roles - and unlike a
/// "*.create" key it is not stripped by the Auditor/ExternalAccountant "Writes" exclusions; "announcements.help.view"
/// is appended explicitly to the roles no wildcard covers.
/// </para>
/// </summary>
public static class AnnouncementsPermissions
{
    private const string Module = "announcements";
    private const string Group = "Announcements";

    public static readonly Permission FeedView = new(
        "announcements.feed.view", Module, "View Announcements", Group, "Read published company announcements and upcoming events");

    public static readonly Permission PostsManage = new(
        "announcements.posts.manage", Module, "Manage Announcements", Group, "Create, edit, publish, archive and pin announcements");

    public static readonly Permission HelpView = new(
        "announcements.help.view", Module, "Help Center", Group, "Search and read Help Center articles");

    public static readonly Permission MyTickets = new(
        "announcements.my.tickets", Module, "Own Support Tickets", Group, "Contact support and view your own support tickets");

    public static IEnumerable<Permission> All =>
    [
        FeedView, PostsManage,
        HelpView, MyTickets,
    ];
}
