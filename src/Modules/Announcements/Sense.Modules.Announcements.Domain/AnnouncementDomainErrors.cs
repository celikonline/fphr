namespace Sense.Modules.Announcements.Domain;

/// <summary>Domain error codes for the Announcements module (kurumsal duyurular).</summary>
public static class AnnouncementDomainErrors
{
    public const string TitleRequired = "announcement.title_required";
    public const string ContentRequired = "announcement.content_required";
    public const string CannotPublish = "announcement.cannot_publish";
    public const string AlreadyArchived = "announcement.already_archived";
    public const string NotFound = "announcement.not_found";
    public const string ReadRecordNotFound = "announcement_read_record.not_found";
}

/// <summary>Domain error codes for the Support subdomain (Yardım Merkezi + Desteğe bağlan, roadmap #69).</summary>
public static class SupportDomainErrors
{
    public const string SubjectRequired = "support_ticket.subject_required";
    public const string BodyRequired = "support_ticket.body_required";
    public const string NotFound = "support_ticket.not_found";
    public const string AlreadyClosed = "support_ticket.already_closed";
    public const string ResolutionRequired = "support_ticket.resolution_required";
}

/// <summary>Length limits shared by Announcements and Support entities.</summary>
public static class AnnouncementsLimits
{
    public const int TitleMaxLength = 200;
    public const int TargetAudienceMaxLength = 200;
    public const int ContentMaxLength = 10_000;
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 10_000;
    public const int ResolutionMaxLength = 5_000;
    public const int SearchTermMaxLength = 200;
}
