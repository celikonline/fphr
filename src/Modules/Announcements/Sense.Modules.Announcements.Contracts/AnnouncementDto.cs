namespace Sense.Modules.Announcements.Contracts;

public sealed record AnnouncementDto(
    Guid Id,
    string Title,
    string Content,
    string Type,
    string Priority,
    string Status,
    Guid PublishedBy,
    DateTime PublishedAt,
    DateTime? ExpiresAt,
    string? TargetAudience);
