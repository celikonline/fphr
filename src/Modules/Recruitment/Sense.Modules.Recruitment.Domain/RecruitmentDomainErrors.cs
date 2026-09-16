namespace Sense.Modules.Recruitment.Domain;

/// <summary>Domain error codes for Recruitment module.</summary>
public static class RecruitmentDomainErrors
{
    public const string PostingNotFound = "recruitment.posting.not_found";
    public const string PostingTitleRequired = "recruitment.posting.title_required";
    public const string PostingNotOpenable = "recruitment.posting.not_openable";
    public const string PostingNotCloseable = "recruitment.posting.not_closeable";
    public const string PostingNotArchivable = "recruitment.posting.not_archivable";
    public const string PostingAlreadyArchived = "recruitment.posting.already_archived";
    public const string PostingRequisitionNotApproved = "recruitment.posting.requisition_not_approved";

    public const string CandidateNotFound = "recruitment.candidate.not_found";
    public const string CandidateNameRequired = "recruitment.candidate.name_required";
    public const string CandidateEmailRequired = "recruitment.candidate.email_required";

    public const string ApplicationNotFound = "recruitment.application.not_found";
    public const string ApplicationAlreadyTerminal = "recruitment.application.already_terminal";

    public const string RequisitionNotFound = "recruitment.requisition.not_found";
    public const string RequisitionTitleRequired = "recruitment.requisition.title_required";
    public const string RequisitionHeadcountInvalid = "recruitment.requisition.headcount_invalid";
    public const string RequisitionNotSubmittable = "recruitment.requisition.not_submittable";
    public const string RequisitionNotPending = "recruitment.requisition.not_pending";
    public const string RequisitionNotCancellable = "recruitment.requisition.not_cancellable";
}

/// <summary>Domain constants and length limits.</summary>
public static class RecruitmentLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 10_000;
    public const int CompanyNotesMaxLength = 2_000;
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 30;
    public const int ApplySlugLength = 22;
}
