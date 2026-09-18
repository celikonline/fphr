using Sense.Modules.Documents.Domain.DocumentTypes;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class DocumentRequirementEvaluatorTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly EmployeeDocumentContext NoSignals = new(
        IsUnder18: false, HasDisability: false, NonTurkishNational: false, HasIncentive: false, UsesDisabilityTaxReduction: false,
        IsExConvictOrTerrorVictim: false, RequiresPeriodicHealthCheck: false, RequiresDriving: false, HasOpenAssets: false,
        IsVeryHazardousJob: false, HasOvertimeConsent: false, IsShiftWorker: false, IsUndergroundWorker: false);

    private static DocumentTypeDefinition Type(
        string code, DocumentTypeStage stage, bool isRequired, DocumentRequirementCondition? condition = null, bool isActive = true)
    {
        var created = DocumentTypeDefinition.Create(TenantId, code, code, stage, isRequired, condition, null, 1, false, null, true, 0).Value;
        if (!isActive)
        {
            created.Deactivate();
        }

        return created;
    }

    [Fact]
    public void ResolveAtHire_IncludesRequiredActiveOnboardingTypes()
    {
        var onboarding = Type("A", DocumentTypeStage.Onboarding, isRequired: true);

        var resolved = DocumentRequirementEvaluator.ResolveAtHire([onboarding], NoSignals);

        resolved.ShouldBe([onboarding]);
    }

    [Fact]
    public void ResolveAtHire_ExcludesOnboardingType_WhenNotRequiredOrInactive()
    {
        var notRequired = Type("A", DocumentTypeStage.Onboarding, isRequired: false);
        var inactive = Type("B", DocumentTypeStage.Onboarding, isRequired: true, isActive: false);

        var resolved = DocumentRequirementEvaluator.ResolveAtHire([notRequired, inactive], NoSignals);

        resolved.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveAtHire_ConditionalType_IncludedOnlyWhenConditionMatches()
    {
        var under18 = Type("ParentalConsent", DocumentTypeStage.Conditional, isRequired: true, DocumentRequirementCondition.AgeUnder18);
        var driving = Type("DriverLicenseCopy", DocumentTypeStage.Conditional, isRequired: true, DocumentRequirementCondition.RequiresDriving);

        var contextMatchingOnlyUnder18 = NoSignals with { IsUnder18 = true };

        var resolved = DocumentRequirementEvaluator.ResolveAtHire([under18, driving], contextMatchingOnlyUnder18);

        resolved.ShouldBe([under18]);
    }

    [Theory]
    [InlineData(DocumentTypeStage.DuringEmployment)]
    [InlineData(DocumentTypeStage.Offboarding)]
    [InlineData(DocumentTypeStage.Recommended)]
    public void ResolveAtHire_NeverIncludesNonOnboardingNonConditionalStages(DocumentTypeStage stage)
    {
        var type = Type("X", stage, isRequired: true);

        var resolved = DocumentRequirementEvaluator.ResolveAtHire([type], NoSignals);

        resolved.ShouldBeEmpty();
    }

    [Fact]
    public void ResolveAtHire_MixOfStages_OnlyOnboardingAndMatchingConditionalSurvive()
    {
        var onboarding = Type("Onboard", DocumentTypeStage.Onboarding, isRequired: true);
        var matchingConditional = Type("Cond1", DocumentTypeStage.Conditional, isRequired: true, DocumentRequirementCondition.HasDisability);
        var nonMatchingConditional = Type("Cond2", DocumentTypeStage.Conditional, isRequired: true, DocumentRequirementCondition.IsShiftWorker);
        var duringEmployment = Type("During", DocumentTypeStage.DuringEmployment, isRequired: false);
        var offboarding = Type("Off", DocumentTypeStage.Offboarding, isRequired: true);
        var recommended = Type("Rec", DocumentTypeStage.Recommended, isRequired: false);

        var context = NoSignals with { HasDisability = true };

        var resolved = DocumentRequirementEvaluator.ResolveAtHire(
            [onboarding, matchingConditional, nonMatchingConditional, duringEmployment, offboarding, recommended], context);

        resolved.ShouldBe([onboarding, matchingConditional], ignoreOrder: true);
    }

    [Fact]
    public void ResolveAtHire_UndergroundWorker_IncludesPeriodicHealthCheckConditional()
    {
        // 6331 md.15: underground işçilerin periyodik sağlık muayenesi gerekir
        var healthCheck = Type("HealthCheck", DocumentTypeStage.Conditional, isRequired: true, DocumentRequirementCondition.RequiresPeriodicHealthCheck);
        var contextUndergroundWorker = NoSignals with { IsUndergroundWorker = true };

        var resolved = DocumentRequirementEvaluator.ResolveAtHire([healthCheck], contextUndergroundWorker);

        resolved.ShouldBe([healthCheck]);
    }

    [Fact]
    public void ResolveAtHire_NonUndergroundWorker_ExcludesPeriodicHealthCheckConditional()
    {
        // Non-underground işçi → health check şartı yok
        var healthCheck = Type("HealthCheck", DocumentTypeStage.Conditional, isRequired: true, DocumentRequirementCondition.RequiresPeriodicHealthCheck);
        var contextNonUnderground = NoSignals with { IsUndergroundWorker = false };

        var resolved = DocumentRequirementEvaluator.ResolveAtHire([healthCheck], contextNonUnderground);

        resolved.ShouldBeEmpty();
    }
}
