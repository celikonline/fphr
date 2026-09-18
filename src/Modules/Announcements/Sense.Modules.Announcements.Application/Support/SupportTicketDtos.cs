namespace Sense.Modules.Announcements.Application.Support;

public sealed record SupportTicketDto(
    Guid Id,
    Guid RequestedByUserId,
    string Subject,
    string Body,
    string Category,
    string Channel,
    string Status,
    string? SourceRoute,
    string? Resolution,
    DateTime? AnsweredAt,
    DateTime? ClosedAt,
    DateTime CreatedAt);
