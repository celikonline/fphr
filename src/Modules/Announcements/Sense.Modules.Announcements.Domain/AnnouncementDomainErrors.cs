using Sense.Shared.Kernel;

namespace Sense.Modules.Announcements.Domain;

public static class AnnouncementDomainErrors
{
    public static readonly Error TitleRequired = Error.Validation(
        "announcements.announcement.title_required",
        "Duyuru başlığı gereklidir ve 200 karakteri aşamaz.");

    public static readonly Error ContentRequired = Error.Validation(
        "announcements.announcement.content_required",
        "Duyuru içeriği gereklidir.");

    public static readonly Error CannotPublish = Error.Conflict(
        "announcements.announcement.cannot_publish",
        "Yalnızca taslak duyurular yayınlanabilir.");

    public static readonly Error AlreadyArchived = Error.Conflict(
        "announcements.announcement.already_archived",
        "Duyuru zaten arşivlenmiş.");

    public static readonly Error NotFound = Error.NotFound(
        "announcements.announcement.not_found",
        "Duyuru bulunamadı.");

    public static readonly Error ReadRecordNotFound = Error.NotFound(
        "announcements.read_record.not_found",
        "Okunma kaydı bulunamadı.");
}
