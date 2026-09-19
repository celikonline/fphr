using Sense.Modules.Announcements.Domain;

namespace Sense.Modules.Announcements.Application.CompanyEvents;

/// <summary>
/// One entry on the company calendar. <see cref="Id"/> is null for derived (Birthday/LeaveStart) entries - they are
/// never persisted, so there is nothing to edit or delete. <see cref="EmployeeId"/> is set only for Birthday/LeaveStart
/// entries (whose title names the employee); manual CompanyEvent/Meeting entries leave it null. <see cref="Type"/> is
/// the enum itself (not pre-converted to string) so JsonStringEnumConverter(CamelCase) serializes it consistently
/// with the rest of the API (CLAUDE.md: "C# enums serialize as camelCase").
/// </summary>
public sealed record CompanyCalendarEntryDto(
    Guid? Id,
    string Title,
    string? Description,
    DateOnly Date,
    CompanyEventType Type,
    Guid? VisibilityOrgUnitId,
    Guid? EmployeeId,
    Guid? CreatedByUserId);
