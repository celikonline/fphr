using Sense.Modules.Announcements.Domain.Support;

namespace Sense.Modules.Announcements.Application.Support;

public sealed record SupportTicketDto(
    Guid Id,
    Guid RequestedByUserId,
    string Subject,
    string Body,
    SupportTicketCategory Category,
    SupportTicketChannel Channel,
    SupportTicketStatus Status,
    string? SourceRoute,
    string? Resolution,
    DateTime? AnsweredAt,
    DateTime? ClosedAt,
    DateTime CreatedAt);
