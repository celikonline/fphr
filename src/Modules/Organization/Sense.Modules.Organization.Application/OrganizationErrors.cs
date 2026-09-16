namespace Sense.Modules.Organization.Application;

/// <summary>Application layer error codes (localized in the Web layer).</summary>
public static class OrganizationErrors
{
    public const string OrgUnitNotFound = "organization.unit.not_found";
    public const string OrgUnitTypeNotFound = "organization.unit_type.not_found";
    public const string OrgUnitTypeInactive = "organization.unit_type.inactive";
    public const string ManagerEmployeeNotFound = "organization.unit.manager_not_found";
    public const string LocationReferenceNotFound = "organization.unit.location_not_found";
    public const string ParentNotFound = "organization.unit.parent_not_found";
    public const string ParentCausesCycle = "organization.unit.parent_cycle";
    public const string HasActiveChildren = "organization.unit.has_active_children";
    public const string UnitTypeInUse = "organization.unit_type.in_use";
    public const string UnitHasActiveEmployees = "organization.unit.has_active_employees";
    public const string CompanyLevelMissing = "organization.unit_type.company_level_missing";
    public const string HolidayDaysOutOfRange = "organization.holidays.days_out_of_range";

    public const string HolidayCalendarNotFound = "organization.holiday_calendar.not_found";
    public const string CompanyNotFound = "organization.company.not_found";
    public const string LocationNotFound = "organization.location.not_found";
    public const string LocationInUse = "organization.location.in_use";

    public const string ApprovalAssignmentRequestTypeUnknown = "organization.approval_assignment.request_type_unknown";
    public const string ApprovalAssignmentDuplicateRequestType = "organization.approval_assignment.duplicate_request_type";
    public const string ApprovalAssignmentOrgUnitInactive = "organization.approval_assignment.org_unit_inactive";
}
