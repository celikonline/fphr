namespace Sense.Modules.Announcements.Domain;

/// <summary>Domain error codes for the Announcements module (kurumsal duyurular).</summary>
public static class AnnouncementDomainErrors
{
    public const string TitleRequired = "announcement.title_required";
    public const string ContentRequired = "announcement.content_required";
    public const string CannotPublish = "announcement.cannot_publish";
    public const string AlreadyArchived = "announcement.already_archived";
    public const string NotFound = "announcement.not_found";
    public const string CannotEditPublished = "announcement.cannot_edit_published";
    public const string ReadRecordNotFound = "announcement_read_record.not_found";
    public const string AttachmentNotFound = "announcement_attachment.not_found";
    public const string AttachmentTypeNotAllowed = "announcement_attachment.type_not_allowed";
    public const string AttachmentTooLarge = "announcement_attachment.too_large";
    public const string TooManyAttachments = "announcement_attachment.too_many";
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

/// <summary>Domain error codes for takdir/teşekkür (Recognition) messages (roadmap #73).</summary>
public static class RecognitionDomainErrors
{
    public const string MessageRequired = "recognition.message_required";
    public const string CannotRecognizeSelf = "recognition.cannot_recognize_self";
    public const string SenderNotFound = "recognition.sender_not_found";
    public const string ReceiverNotFound = "recognition.receiver_not_found";
}

/// <summary>Length limits shared by Announcements and Support entities.</summary>
public static class AnnouncementsLimits
{
    public const int TitleMaxLength = 200;
    public const int TargetAudienceMaxLength = 200;
    /// <summary>Zengin metin (HTML) içerik; sunucu tarafında temizlendikten sonra bu sınırla ölçülür - düz metnin
    /// çok üzerinde çünkü etiketler/öznitelikler de uzunluğa dahildir (gömülü resimler ise ayrı ek dosyalardır, bkz.
    /// AnnouncementAttachment - içerikte yalnızca <c>data-attachment-id</c> referansı tutulur, base64 gömülmez).</summary>
    public const int ContentMaxLength = 50_000;
    public const int MaxAttachmentSizeBytes = 10 * 1024 * 1024;
    public const int MaxAttachmentsPerAnnouncement = 20;
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 10_000;
    public const int ResolutionMaxLength = 5_000;
    public const int SearchTermMaxLength = 200;
    /// <summary>Takdir/teşekkür mesajı; "kısa" bir not olması istendiği için duyuru içeriğinden çok daha dar bir sınır.</summary>
    public const int RecognitionMessageMaxLength = 300;
}
