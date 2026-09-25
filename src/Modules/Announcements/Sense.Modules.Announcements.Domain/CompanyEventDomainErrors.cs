namespace Sense.Modules.Announcements.Domain;

/// <summary>Domain error codes for the company calendar subdomain (senseik-027, roadmap #71/P-19).</summary>
public static class CompanyEventDomainErrors
{
    public const string TitleRequired = "company_event.title_required";
    public const string DescriptionTooLong = "company_event.description_too_long";
    public const string TypeNotManual = "company_event.type_not_manual";
    public const string NotFound = "company_event.not_found";
    public const string EndsBeforeStarts = "company_event.ends_before_starts";
    public const string OnlineUrlRequired = "company_event.online_url_required";
    public const string LocationRequired = "company_event.location_required";
    public const string LocationTooLong = "company_event.location_too_long";
    public const string CategoryNotFound = "company_event.category_not_found";
    public const string InviteeNotFound = "company_event.invitee_not_found";
    public const string CategoryNameRequired = "company_event_category.name_required";
    public const string CategoryColorInvalid = "company_event_category.color_invalid";
    public const string CategoryInUse = "company_event_category.in_use";
    public const string CategoryDuplicateName = "company_event_category.duplicate_name";
}

/// <summary>Length limits for <see cref="CompanyEvent"/>.</summary>
public static class CompanyEventLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2_000;
    public const int LocationMaxLength = 300;
    public const int OnlineUrlMaxLength = 500;
    public const int CategoryNameMaxLength = 60;
    public const int MaxInvitees = 500;

    /// <summary>Month-view grid with padding days never needs more than this; keeps the merge query cheap.</summary>
    public const int CalendarMaxRangeDays = 62;
}
