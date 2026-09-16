using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Recruitment.Contracts;

/// <summary>
/// Recruitment module permission definitions (R0: postings/candidates/applications only). SystemRoleDefinitions.cs
/// already grants "recruitment.*" to HR/HRSpecialist/Recruiter, so no SystemRoleDefinitions.cs change is needed for
/// the keys below; Interviewer/Manager's narrower "recruitment.interviews.*"/"recruitment.scorecards.*" keys belong
/// to later Recruitment packages (interviews/scorecards are not modeled yet).
/// </summary>
public static class RecruitmentPermissions
{
    private const string Module = "recruitment";
    private const string Group = "Recruitment";

    public static readonly Permission PostingsRead = new(
        "recruitment.postings.read", Module, "Read Job Postings", Group, "View job postings");

    public static readonly Permission PostingsCreate = new(
        "recruitment.postings.create", Module, "Create Job Posting", Group, "Create a job posting");

    public static readonly Permission PostingsUpdate = new(
        "recruitment.postings.update", Module, "Update Job Posting", Group, "Edit a job posting's fields, publish/close/reopen it, or change its access list");

    public static readonly Permission PostingsArchive = new(
        "recruitment.postings.archive", Module, "Archive Job Posting", Group, "Archive a job posting (terminal)");

    public static readonly Permission CandidatesRead = new(
        "recruitment.candidates.read", Module, "Read Candidates", Group, "View candidates");

    public static readonly Permission CandidatesManage = new(
        "recruitment.candidates.manage", Module, "Manage Candidates", Group, "Add or edit a candidate's contact information");

    public static readonly Permission ApplicationsRead = new(
        "recruitment.applications.read", Module, "Read Applications", Group, "View a job posting's or candidate's applications");

    public static readonly Permission ApplicationsManage = new(
        "recruitment.applications.manage", Module, "Manage Applications", Group, "Add an application, or move it between pipeline stages");

    public static readonly Permission RequisitionsRead = new(
        "recruitment.requisitions.read", Module, "Read Job Requisitions", Group, "View job requisitions");

    public static readonly Permission RequisitionsManage = new(
        "recruitment.requisitions.manage", Module, "Manage Job Requisitions", Group, "Create, submit for approval, or cancel a job requisition");

    public static IEnumerable<Permission> All =>
        [
            PostingsRead, PostingsCreate, PostingsUpdate, PostingsArchive, CandidatesRead, CandidatesManage,
            ApplicationsRead, ApplicationsManage, RequisitionsRead, RequisitionsManage,
        ];
}
