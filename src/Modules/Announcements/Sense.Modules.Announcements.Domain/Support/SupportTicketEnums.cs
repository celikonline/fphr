namespace Sense.Modules.Announcements.Domain.Support;

/// <summary>Talep konu kategorisi (11.3.13: Yardım Merkezi kategorileri ile hizalı, serbest metin yerine sabit liste).</summary>
public enum SupportTicketCategory
{
    General = 0,
    DataTransfer = 1,
    LeaveAndOvertime = 2,
    Approvals = 3,
    AccessAndPermissions = 4,
    Payroll = 5,
    Billing = 6,
    Other = 7,
}

/// <summary>Talebin geldiği kanal (11.3.13: "Desteğe bağlan" — canlı sohbet henüz yok, Faz 4'te yalnız Ticket).</summary>
public enum SupportTicketChannel
{
    Ticket = 0,
    Email = 1,
    LiveChat = 2,
}

/// <summary>11.3.13: SupportTicket{..., Status(Open/Answered/Closed), ...}.</summary>
public enum SupportTicketStatus
{
    Open = 0,
    Answered = 1,
    Closed = 2,
}
