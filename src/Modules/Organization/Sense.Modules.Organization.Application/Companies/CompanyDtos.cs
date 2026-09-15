namespace Sense.Modules.Organization.Application.Companies;

/// <summary>Company settings row attached to the tenant's root org unit (see Domain.Companies.Company's doc comment).</summary>
public sealed record CompanyDto(Guid Id, Guid OrgUnitId, Guid? HolidayCalendarId, string? JointEmploymentGroupCode);
