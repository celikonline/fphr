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
    private const string Group = "Bildirimler";

    /// <summary>List one's own in-app notifications and unread count.</summary>
    public static readonly Permission MyView = new(
        "notifications.my.view", Module, "Kendi Bildirimlerimi Görüntüleme", Group, "Kendi uygulama içi bildirimlerinizi ve bildirim ayarlarınızı görüntüleme");

    /// <summary>Mark one's own notifications read and change one's own notification settings.</summary>
    public static readonly Permission MyManage = new(
        "notifications.my.manage", Module, "Kendi Bildirimlerimi Yönetme", Group, "Kendi bildirimlerinizi okundu olarak işaretleme ve hangi bildirimleri alacağınızı değiştirme");

    public static readonly Permission MessagesSend = new(
        "notifications.messages.send", Module, "İK Mesajı Gönderme", Group, "Çalışanlara e-posta / uygulama içi İK mesajı gönderme");

    public static readonly Permission MessagesView = new(
        "notifications.messages.view", Module, "İK Mesajlarını Görüntüleme", Group, "Gönderilen ve alınan İK mesajlarını görüntüleme");

    public static readonly Permission MessageGroupsManage = new(
        "notifications.messagegroups.manage", Module, "Mesaj Gruplarını Yönetme", Group, "Kalıcı mesaj alıcı gruplarını oluşturma, düzenleme ve silme");

    public static IEnumerable<Permission> All => [MyView, MyManage, MessagesSend, MessagesView, MessageGroupsManage];
}
