using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Announcements.Contracts;

/// <summary>
/// Announcements module permissions (kurumsal duyurular + Support alt alanı, roadmap #69). Keys are exactly
/// module.resource.action / module.my.resource (3 segments, snake_case) per Sense.Tests.Architecture.
/// OnionArchitectureTests.PermissionKeys_FollowNamingConvention. SystemRoleDefinitions.cs already grants
/// "announcements.*" to Owner/Admin/HR/HRSpecialist, "announcements.feed.*" to Employee/Intern and "*.my.*"
/// (self-service) to every base role, so no role-mapping change is needed for these new keys.
/// </summary>
public static class AnnouncementsPermissions
{
    private const string Module = "announcements";
    private const string Group = "Duyurular";
    private const string SupportGroup = "Destek";

    public static readonly Permission View = new(
        "announcements.announcements.view", Module, "Duyuruları görüntüleme", Group, "Kurumsal duyuruları görüntüleme");

    public static readonly Permission Manage = new(
        "announcements.announcements.manage", Module, "Duyuru yönetimi", Group, "Duyuru oluşturma, yayınlama ve arşivleme");

    /// <summary>Kendi destek taleplerini oluşturma/görme; "*.my.*" ile her temel role otomatik ulaşır.</summary>
    public static readonly Permission MySupportTickets = new(
        "announcements.my.support_tickets", Module, "Destek taleplerim", SupportGroup, "Kendi destek talebini oluşturma ve görüntüleme");

    /// <summary>Kiracının tüm destek taleplerini görme ve yanıtlama (İK/Admin).</summary>
    public static readonly Permission ManageSupportTickets = new(
        "announcements.support_tickets.manage", Module, "Destek taleplerini yönetme", SupportGroup, "Kiracının tüm destek taleplerini görüntüleme, yanıtlama ve kapatma");

    public static IEnumerable<Permission> All =>
        [View, Manage, MySupportTickets, ManageSupportTickets];
}
