namespace Sense.Modules.Organization.Application.Companies;

/// <summary>Company settings row attached to a Company-level org unit; Name is the org unit's name (see Domain.Companies.Company).</summary>
public sealed record CompanyDto(
    Guid Id,
    Guid OrgUnitId,
    string Name,
    Guid? HolidayCalendarId,
    string? HolidayCalendarName,
    string? JointEmploymentGroupCode);
