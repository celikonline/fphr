namespace Sense.Modules.Announcements.Domain;

/// <summary>Domain error codes for the Announcements module (announcements + Support sub-area).</summary>
public static class AnnouncementsDomainErrors
{
    public const string NotFound = "announcement.not_found";
    public const string TitleRequired = "announcement.title_required";
    public const string BodyRequired = "announcement.body_required";
    public const string EventStartRequired = "announcement.event_start_required";
    public const string EventFieldsNotAllowed = "announcement.event_fields_not_allowed";
    public const string EventEndBeforeStart = "announcement.event_end_before_start";
    public const string MeetingUrlInvalid = "announcement.meeting_url_invalid";
    public const string NotDraft = "announcement.not_draft";
    public const string NotPublished = "announcement.not_published";
    public const string AlreadyArchived = "announcement.already_archived";

    public const string HelpArticleNotFound = "help_article.not_found";
    public const string HelpArticleSlugRequired = "help_article.slug_required";
    public const string HelpArticleTitleRequired = "help_article.title_required";
    public const string HelpArticleLocaleInvalid = "help_article.locale_invalid";

    public const string TicketSubjectRequired = "support_ticket.subject_required";
    public const string TicketBodyRequired = "support_ticket.body_required";
}

/// <summary>Domain constants and length limits.</summary>
public static class AnnouncementsLimits
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 20000;
    public const int LocationMaxLength = 300;
    public const int UrlMaxLength = 1000;

    public const int SlugMaxLength = 150;
    public const int SummaryMaxLength = 500;
    public const int HelpBodyMaxLength = 50000;
    public const int TagMaxLength = 60;
    public const int RouteMaxLength = 200;

    public const int TicketSubjectMaxLength = 200;
    public const int TicketBodyMaxLength = 5000;
    public const int PageContextMaxLength = 500;

    /// <summary>Default look-ahead window of the home "Etkinlik takvimi" widget (days).</summary>
    public const int UpcomingEventsDefaultDays = 30;
    public const int UpcomingEventsMaxDays = 365;
}

/// <summary>Help Center content locales (tr/en content, per 11.3.13).</summary>
public static class HelpLocales
{
    public const string Turkish = "tr";
    public const string English = "en";
    public const string Default = Turkish;

    public static bool IsSupported(string? locale) => locale is Turkish or English;

    /// <summary>Maps "en-US"/"EN"/"tr-TR" etc. to a supported content locale, or null when unsupported/empty.</summary>
    public static string? Normalize(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        var twoLetter = locale.Trim().Split('-', '_')[0].ToLowerInvariant();
        return IsSupported(twoLetter) ? twoLetter : null;
    }
}
