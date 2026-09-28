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
    public const string CandidatePhoneInvalid = "recruitment.candidate.phone_invalid";
    public const string CandidateAlreadyAnonymized = "recruitment.candidate.already_anonymized";

    public const string ApplicationNotFound = "recruitment.application.not_found";
    public const string ApplicationAlreadyTerminal = "recruitment.application.already_terminal";
    public const string ApplicationAlreadyExists = "recruitment.application.already_exists";

    /// <summary>
    /// Career-site apply body's <c>privacyNoticeVersion</c> isn't the product-wide current aydınlatma metni version
    /// (<see cref="RecruitmentLimits.KnownPrivacyNoticeVersion"/>) - PO kararı, Faz 9 gün açılışı TALIMAT'ı.
    /// </summary>
    public const string ApplicationPrivacyNoticeOutdated = "recruitment.application.privacy_notice_outdated";

    public const string RequisitionNotFound = "recruitment.requisition.not_found";
    public const string RequisitionTitleRequired = "recruitment.requisition.title_required";
    public const string RequisitionHeadcountInvalid = "recruitment.requisition.headcount_invalid";
    public const string RequisitionNotSubmittable = "recruitment.requisition.not_submittable";
    public const string RequisitionNotPending = "recruitment.requisition.not_pending";
    public const string RequisitionNotCancellable = "recruitment.requisition.not_cancellable";

    public const string RequisitionFieldTemplateRulesRequired = "recruitment.requisition_field_template.rules_required";
    public const string RequisitionFieldTemplateDuplicateField = "recruitment.requisition_field_template.duplicate_field";
    public const string RequisitionFieldTemplateRequiredFieldMustBeVisible = "recruitment.requisition_field_template.required_field_must_be_visible";
    public const string RequisitionFieldTemplateDefaultValueRequired = "recruitment.requisition_field_template.default_value_required";
    public const string RequisitionEmploymentTypeRequired = "recruitment.requisition.employment_type_required";
    public const string RequisitionReasonRequired = "recruitment.requisition.reason_required";
    public const string RequisitionJustificationRequired = "recruitment.requisition.justification_required";

    public const string TagNotFound = "recruitment.tag.not_found";
    public const string TagNameRequired = "recruitment.tag.name_required";
    public const string TagAlreadyInactive = "recruitment.tag.already_inactive";

    public const string FormTemplateNotFound = "recruitment.form_template.not_found";
    public const string FormTemplateNameRequired = "recruitment.form_template.name_required";
    public const string FormTemplateFieldsRequired = "recruitment.form_template.fields_required";
    public const string FormTemplateAlreadyInactive = "recruitment.form_template.already_inactive";
    public const string FormFieldLabelRequired = "recruitment.form_field.label_required";
    public const string FormFieldOptionsRequired = "recruitment.form_field.options_required";
    public const string ScorecardRequiresScoreFields = "recruitment.form_template.scorecard_requires_score_fields";
    public const string ScorecardWeightsMustSumTo100 = "recruitment.form_template.scorecard_weights_must_sum_to_100";

    public const string FormResponseFieldRequired = "recruitment.form_response.field_required";
    public const string FormResponseScoreInvalid = "recruitment.form_response.score_invalid";

    public const string AutomationRuleNotFound = "recruitment.automation_rule.not_found";
    public const string AutomationRuleSameStage = "recruitment.automation_rule.same_stage";
    public const string AutomationRuleAlreadyInactive = "recruitment.automation_rule.already_inactive";
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
    public const int FormAnswerMaxLength = 4_000;

    /// <summary>Career-site application's aydınlatma metni version tag (e.g. "2026-09"), Faz 9 tur 3 TALIMAT madde 2.</summary>
    public const int PrivacyNoticeVersionMaxLength = 20;

    /// <summary>
    /// Product-wide current aydınlatma metni version (PO kararı, Faz 9 gün açılışı TALIMAT'ı 2026-09-16: "Aydınlatma
    /// metni versiyonu ürün genelinde '2026-09'"). A career-site apply carrying any other value fails with
    /// <see cref="RecruitmentDomainErrors.ApplicationPrivacyNoticeOutdated"/> - the candidate saw a stale page.
    /// </summary>
    public const string KnownPrivacyNoticeVersion = "2026-09";

    /// <summary>
    /// KVKK data retention window for a candidate's personal data (Faz 9 "KVKK uyumlu aday verisi saklama ve imha").
    /// After this many months from <c>CreatedAt</c> a candidate not hired is eligible for anonymization; see
    /// <see cref="Sense.Modules.Recruitment.Domain.Candidates.Candidate.Anonymize"/>. No dedicated retention/consent
    /// column exists yet - deliberately derived from the existing audit <c>CreatedAt</c> column (no migration this
    /// slice, see ADR-0001 "elle migration yasak"); revisit once EF tooling is back and Faz 1's ConsentRecord ships.
    /// </summary>
    public const int CandidateDataRetentionMonths = 24;
}
