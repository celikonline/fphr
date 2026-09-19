namespace Sense.Modules.Announcements.Domain;

/// <summary>Domain error codes for the company calendar subdomain (senseik-027, roadmap #71/P-19).</summary>
public static class CompanyEventDomainErrors
{
    public const string TitleRequired = "company_event.title_required";
    public const string DescriptionTooLong = "company_event.description_too_long";
    public const string TypeNotManual = "company_event.type_not_manual";
    public const string NotFound = "company_event.not_found";
}

/// <summary>Length limits for <see cref="CompanyEvent"/>.</summary>
public static class CompanyEventLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2_000;

    /// <summary>Month-view grid with padding days never needs more than this; keeps the merge query cheap.</summary>
    public const int CalendarMaxRangeDays = 62;
}
