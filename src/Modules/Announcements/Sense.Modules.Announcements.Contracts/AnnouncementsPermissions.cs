using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Announcements.Contracts;

public static class AnnouncementsPermissions
{
    private const string Module = "announcements";
    private const string Group = "Duyurular";

    public static readonly Permission View = new(
        "announcements.view", Module, "Duyuruları Görüntüleme", Group, "Kurumsal duyuruları görüntüleme");

    public static readonly Permission Manage = new(
        "announcements.manage", Module, "Duyuru Yönetimi", Group, "Duyuru oluşturma, yayınlama ve arşivleme");

    public static IEnumerable<Permission> All =>
        [View, Manage];
}
