using Sense.Shared.Contracts.Results;

namespace Sense.Modules.Announcements.Application;

public static class AnnouncementsErrors
{
    public static readonly Error AnnouncementNotFound = new("announcements.not_found", "Announcement not found.");
    public static readonly Error TitleRequired = new("announcements.title_required", "Title is required.");
    public static readonly Error ContentRequired = new("announcements.content_required", "Content is required.");
}
