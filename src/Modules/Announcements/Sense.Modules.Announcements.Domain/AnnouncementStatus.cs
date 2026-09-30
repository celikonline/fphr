namespace Sense.Modules.Announcements.Domain;

public enum AnnouncementStatus
{
    Draft = 0,
    Published = 1,
    Expired = 2,
    Archived = 3,
    /// <summary>Oluşturulurken bir ScheduledFor (gelecek tarih/saat) verildi; PublishScheduledAnnouncementsJob zamanı
    /// gelince (ScheduledFor &lt;= now) otomatik <see cref="Published"/>'e geçirir (TK-016).</summary>
    Scheduled = 4
}
