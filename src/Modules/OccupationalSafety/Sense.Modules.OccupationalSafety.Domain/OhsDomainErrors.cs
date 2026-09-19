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
}

/// <summary>Domain constants and length limits.</summary>
public static class OhsLimits
{
    public const int NoteMaxLength = 1000;
    public const int PreparedByMaxLength = 255;
}
