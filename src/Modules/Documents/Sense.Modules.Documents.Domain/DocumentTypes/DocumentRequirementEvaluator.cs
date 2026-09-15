namespace Sense.Modules.Documents.Domain.DocumentTypes;

/// <summary>Boolean signals about one employee that a <see cref="DocumentTypeStage.Conditional"/> type's <see cref="DocumentRequirementCondition"/> is matched against.</summary>
public sealed record EmployeeDocumentContext(
    bool IsUnder18,
    bool HasDisability,
    bool NonTurkishNational,
    bool HasIncentive,
    bool UsesDisabilityTaxReduction,
    bool IsExConvictOrTerrorVictim,
    bool RequiresPeriodicHealthCheck,
    bool RequiresDriving,
    bool HasOpenAssets,
    bool IsVeryHazardousJob,
    bool HasOvertimeConsent,
    bool IsShiftWorker)
{
    public bool Matches(DocumentRequirementCondition condition) => condition switch
    {
        DocumentRequirementCondition.AgeUnder18 => IsUnder18,
        DocumentRequirementCondition.HasDisability => HasDisability,
        DocumentRequirementCondition.NonTurkishNational => NonTurkishNational,
        DocumentRequirementCondition.HasIncentive => HasIncentive,
        DocumentRequirementCondition.UsesDisabilityTaxReduction => UsesDisabilityTaxReduction,
        DocumentRequirementCondition.ExConvictOrTerrorVictim => IsExConvictOrTerrorVictim,
        DocumentRequirementCondition.RequiresPeriodicHealthCheck => RequiresPeriodicHealthCheck,
        DocumentRequirementCondition.RequiresDriving => RequiresDriving,
        DocumentRequirementCondition.HasOpenAssets => HasOpenAssets,
        DocumentRequirementCondition.VeryHazardousJob => IsVeryHazardousJob,
        DocumentRequirementCondition.HasOvertimeConsent => HasOvertimeConsent,
        DocumentRequirementCondition.IsShiftWorker => IsShiftWorker,
        _ => false,
    };
}

/// <summary>Pure domain service: resolves which document types apply to an employee. No I/O.</summary>
public static class DocumentRequirementEvaluator
{
    /// <summary>
    /// Onboarding + Conditional active/required types applicable to this employee at hire time. DuringEmployment/
    /// Offboarding/Recommended stages are not resolved here (event/wizard driven, out of scope for now).
    /// </summary>
    public static IReadOnlyList<DocumentTypeDefinition> ResolveAtHire(
        IReadOnlyCollection<DocumentTypeDefinition> activeTypes, EmployeeDocumentContext context) =>
        activeTypes.Where(t => t.IsActive && t.IsRequired &&
                (t.Stage == DocumentTypeStage.Onboarding ||
                 (t.Stage == DocumentTypeStage.Conditional && t.Condition.HasValue && context.Matches(t.Condition.Value))))
            .ToList();
}
