namespace Sense.Modules.Announcements.Application;

/// <summary>Application-layer error codes (handler-level checks not owned by the domain model).</summary>
public static class AnnouncementsErrors
{
    /// <summary>Bir kullanıcı başkasının açtığı destek talebini görmeye çalıştığında (varlık sızıntısı olmasın diye NotFound olarak dönülür).</summary>
    public const string TicketNotFound = "support_ticket.not_found";
}
