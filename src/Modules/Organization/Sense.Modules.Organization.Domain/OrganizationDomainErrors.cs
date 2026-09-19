namespace Sense.Modules.Organization.Domain;

/// <summary>Domain error codes for Organization module.</summary>
public static class OrganizationDomainErrors
{
    public const string OrgUnitTypeNotFound = "organization.unit_type.not_found";
    public const string OrgUnitTypeCodeRequired = "organization.unit_type.code_required";
    public const string OrgUnitTypeNameRequired = "organization.unit_type.name_required";
    public const string OrgUnitTypeSystemImmutable = "organization.unit_type.system_immutable";
    public const string OrgUnitTypeNameTooLong = "organization.unit_type.name_too_long";
    public const string OrgUnitTypeAlreadyInactive = "organization.unit_type.already_inactive";
    public const string OrgUnitTypeAlreadyActive = "organization.unit_type.already_active";
    public const string OrgUnitTypeLimitReached = "organization.unit_type.limit_reached";
    public const string OrgUnitTypeInsertPositionInvalid = "organization.unit_type.insert_position_invalid";
    public const string OrgUnitTypeOrderInvalid = "organization.unit_type.order_invalid";
    public const string OrgUnitTypeCompanyMustBeFirst = "organization.unit_type.company_must_be_first";

    public const string OrgUnitNotFound = "organization.unit.not_found";
    public const string OrgUnitNameRequired = "organization.unit.name_required";
    public const string OrgUnitTypeIdRequired = "organization.unit.type_id_required";
    public const string OrgUnitAlreadyInactive = "organization.unit.already_inactive";

    public const string CompanyNotFound = "organization.company.not_found";
    public const string CompanyOrgUnitRequired = "organization.company.org_unit_required";

    public const string ApprovalAssignmentOrgUnitRequired = "organization.approval_assignment.org_unit_required";
    public const string ApprovalAssignmentRequestTypeRequired = "organization.approval_assignment.request_type_required";
    public const string ApprovalAssignmentPolicyRequired = "organization.approval_assignment.policy_id_required";
    public const string ApprovalAssignmentRequestTypeInvalid = "organization.approval_assignment.request_type_invalid";

    public const string HolidayCalendarNotFound = "organization.holiday_calendar.not_found";
    public const string HolidayCalendarNameRequired = "organization.holiday_calendar.name_required";
    public const string HolidayCalendarCountryCodeRequired = "organization.holiday_calendar.country_code_required";

    public const string HolidayCalendarEntryCalendarIdRequired = "organization.holiday_calendar_entry.calendar_id_required";
    public const string HolidayCalendarEntryNameRequired = "organization.holiday_calendar_entry.name_required";

    public const string LocationNotFound = "organization.location.not_found";
    public const string LocationNameRequired = "organization.location.name_required";
    public const string LocationTypeInvalid = "organization.location.type_invalid";
    public const string LocationCountryCodeInvalid = "organization.location.country_code_invalid";
    public const string LocationAlreadyInactive = "organization.location.already_inactive";
    public const string LocationGeofenceInvalid = "organization.location.geofence_invalid";

    public const string CostCenterNotFound = "organization.cost_center.not_found";
    public const string CostCenterCodeRequired = "organization.cost_center.code_required";
    public const string CostCenterCodeTooLong = "organization.cost_center.code_too_long";
    public const string CostCenterNameRequired = "organization.cost_center.name_required";
    public const string CostCenterCodeTaken = "organization.cost_center.code_taken";
    public const string CostCenterAlreadyActive = "organization.cost_center.already_active";
    public const string CostCenterAlreadyInactive = "organization.cost_center.already_inactive";
}

/// <summary>Domain constants and length limits.</summary>
public static class OrganizationLimits
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int RequestTypeMaxLength = 50;
    public const int CountryCodeMaxLength = 10;
    public const int AddressMaxLength = 500;

    /// <summary>ISO 3166-1 alpha-2 country code length (e.g. "TR").</summary>
    public const int CountryCodeLength = 2;

    /// <summary>Default cap on how many hierarchy levels (OrgUnitType rows) a tenant may define; see IOptions&lt;OrganizationOptions&gt; (future item).</summary>
    public const int MaxUnitTypesDefault = 10;

    /// <summary>Company profile field limits (Faz 2, TB-10).</summary>
    public const int RegistrationNumberMaxLength = 50;

    public const int TaxNumberMaxLength = 50;

    public const int CityMaxLength = 100;

    public const int CountryMaxLength = 100;

    /// <summary>Max length of the stored CSV of weekday codes (7 x "xxx," fits comfortably).</summary>
    public const int WorkingDaysCsvMaxLength = 40;

    public const int LogoUrlMaxLength = 1000;

    /// <summary>Cost center code (Faz 2, S-4); short and immutable once created (referenced by Payroll/Analytics later).</summary>
    public const int CostCenterCodeMaxLength = 30;

    /// <summary>SGK "işyeri sicil numarası" (APHB, senseik-010/F-03); free text, no fixed structure enforced here.</summary>
    public const int SgkIsyeriSicilNoMaxLength = 30;
}
