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
    private const string CalendarGroup = "Etkinlik Takvimi";

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

    /// <summary>
    /// Şirket etkinlik takvimini görüntüleme (senseik-027, roadmap #71/P-19 kararındaki anahtar adı:
    /// "announcements.my.events"). "*.my.*" deseni ile PermissionPatterns.SelfService zaten her temel role
    /// ulaşır (SystemRoleDefinitions.cs'te her rol ".. PermissionPatterns.SelfService" içerir), bu yüzden ayrı bir
    /// rol eşleme değişikliği gerekmez.
    /// </summary>
    public static readonly Permission ViewCalendar = new(
        "announcements.my.events", Module, "Etkinlik takvimini görüntüleme", CalendarGroup, "Şirket etkinlik takvimini (doğum günleri, izin başlangıçları, manuel etkinlikler) görüntüleme");

    /// <summary>Manuel etkinlik oluşturma/silme (İK/Admin) - roadmap #71/P-19 kararındaki anahtar adı: "announcements.events.manage".</summary>
    public static readonly Permission ManageCalendar = new(
        "announcements.events.manage", Module, "Etkinlik yönetimi", CalendarGroup, "Şirket etkinliği oluşturma ve silme");

    /// <summary>
    /// Çalışanın duyuru akışı (yayındaki duyurular + okundu işareti). "announcements.feed.*" hiçbir gerçek anahtarla
    /// eşleşmediği için çalışanlar duyuru göremiyordu; "*.my.*" deseniyle her temel role otomatik ulaşır.
    /// </summary>
    public static readonly Permission MyFeed = new(
        "announcements.my.feed", Module, "Duyuru akışım", Group, "Yayındaki duyuruları okuma ve okundu olarak işaretleme");

    public static IEnumerable<Permission> All =>
        [View, Manage, MySupportTickets, ManageSupportTickets, ViewCalendar, ManageCalendar, MyFeed];
}
