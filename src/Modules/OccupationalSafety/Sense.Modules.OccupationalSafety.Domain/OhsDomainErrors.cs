namespace Sense.Modules.OccupationalSafety.Domain;

/// <summary>Domain error codes for the Occupational Safety (İSG) module.</summary>
public static class OhsDomainErrors
{
    public const string HazardClassificationNotFound = "ohs_hazard_classification.not_found";
    public const string HazardClassificationInvalid = "ohs_hazard_classification.invalid";
    public const string HazardClassificationLocationRequired = "ohs_hazard_classification.location_required";
    public const string HazardClassificationEffectiveDateDuplicate = "ohs_hazard_classification.effective_date_duplicate";

    public const string RiskAssessmentNotFound = "ohs_risk_assessment.not_found";
    public const string RiskAssessmentLocationRequired = "ohs_risk_assessment.location_required";
    public const string RiskAssessmentHazardClassificationMissing = "ohs_risk_assessment.hazard_classification_missing";
    public const string RiskAssessmentPreparedByRequired = "ohs_risk_assessment.prepared_by_required";

    public const string IncidentNotFound = "ohs_incident.not_found";
    public const string IncidentKindInvalid = "ohs_incident.kind_invalid";
    public const string IncidentDateInFuture = "ohs_incident.date_in_future";
    public const string IncidentDescriptionRequired = "ohs_incident.description_required";
    public const string IncidentEmployeeRequired = "ohs_incident.employee_required";
    public const string IncidentEmployeeNotFound = "ohs_incident.employee_not_found";
    public const string IncidentSgkDeadlineRequired = "ohs_incident.sgk_deadline_required";
    public const string IncidentSgkNotApplicable = "ohs_incident.sgk_not_applicable";
    public const string IncidentSgkDateInvalid = "ohs_incident.sgk_date_invalid";
    public const string IncidentSgkNotNotified = "ohs_incident.sgk_not_notified";
    public const string IncidentCorrectiveActionRequired = "ohs_incident.corrective_action_required";
    public const string IncidentAlreadyClosed = "ohs_incident.already_closed";
    public const string IncidentReporterNotEmployee = "ohs_incident.reporter_not_employee";
}

/// <summary>Domain constants and length limits.</summary>
public static class OhsLimits
{
    public const int NoteMaxLength = 1000;
    public const int PreparedByMaxLength = 255;
    public const int IncidentDescriptionMaxLength = 2000;
}
