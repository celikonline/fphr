using Sense.Modules.Announcements.Domain.Announcements;

namespace Sense.Modules.Announcements.Application.Announcements;

public sealed record AnnouncementDto(
    Guid Id,
    string Title,
    string Body,
    AnnouncementCategory Category,
    AnnouncementStatus Status,
    bool IsPinned,
    DateTime? PublishedAt,
    DateTime? EventStartsAt,
    DateTime? EventEndsAt,
    string? Location,
    string? MeetingUrl,
    DateTime CreatedAt);

/// <summary>Home "Etkinlik takvimi" widget row (11.3.13 EventCalendar{Başlık, Başlangıç, Bitiş, Konum/Link}).</summary>
public sealed record UpcomingEventDto(Guid Id, string Title, DateTime StartsAt, DateTime? EndsAt, string? Location, string? MeetingUrl);

internal static class AnnouncementMapper
{
    public static AnnouncementDto ToDto(Announcement a) =>
        new(
            a.Id, a.Title, a.Body, a.Category, a.Status, a.IsPinned, a.PublishedAt,
            a.EventStartsAt, a.EventEndsAt, a.Location, a.MeetingUrl, a.CreatedAt);

    public static UpcomingEventDto ToUpcomingEvent(Announcement a) =>
        new(a.Id, a.Title, a.EventStartsAt!.Value, a.EventEndsAt, a.Location, a.MeetingUrl);
}
