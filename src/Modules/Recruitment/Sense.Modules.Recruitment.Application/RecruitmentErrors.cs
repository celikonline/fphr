namespace Sense.Modules.Recruitment.Application;

/// <summary>Application layer error codes (localized in the Web layer).</summary>
public static class RecruitmentErrors
{
    public const string OrgUnitInvalid = "recruitment.posting.org_unit_invalid";
    public const string LocationInvalid = "recruitment.posting.location_invalid";
    public const string OwnerUserInvalid = "recruitment.posting.owner_invalid";
    public const string HiringManagerUserInvalid = "recruitment.posting.hiring_manager_invalid";

    public const string NoEmployeeForUser = "recruitment.requisition.no_employee_for_user";
    public const string RequisitionNotApproved = "recruitment.posting.requisition_not_approved";
}
