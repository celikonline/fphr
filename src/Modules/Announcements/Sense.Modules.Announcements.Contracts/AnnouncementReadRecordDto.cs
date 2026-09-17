namespace Sense.Modules.Announcements.Contracts;

public sealed record AnnouncementReadRecordDto(
    Guid Id,
    Guid AnnouncementId,
    Guid EmployeeId,
    DateTime ReadAt);
