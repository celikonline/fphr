namespace Sense.Modules.Announcements.Application;

/// <summary>Application-layer error codes (handler-level checks not owned by the domain model).</summary>
public static class AnnouncementsErrors
{
    /// <summary>Bir kullanıcı başkasının açtığı destek talebini görmeye çalıştığında (varlık sızıntısı olmasın diye NotFound olarak dönülür).</summary>
    public const string TicketNotFound = "support_ticket.not_found";

    /// <summary>Şirket takvimi sorgusunda bitiş tarihi başlangıçtan önce (senseik-027).</summary>
    public const string CalendarInvalidRange = "company_calendar.invalid_range";

    /// <summary>Şirket takvimi sorgusu CompanyEventLimits.CalendarMaxRangeDays'i aşıyor (senseik-027).</summary>
    public const string CalendarRangeTooLarge = "company_calendar.range_too_large";

    /// <summary>Manuel etkinlik oluşturulurken verilen VisibilityOrgUnitId aktif bir org birimine karşılık gelmiyor (senseik-027).</summary>
    public const string VisibilityOrgUnitNotFound = "company_event.visibility_org_unit_not_found";
}
