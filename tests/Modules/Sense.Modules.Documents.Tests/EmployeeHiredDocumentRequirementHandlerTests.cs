using NSubstitute;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
using Sense.Modules.Payroll.Contracts;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>
/// EmployeeHired → materialization: verifies which document types get resolved for a hire with IsUnder18=true, and
/// that an already-materialized requirement is not duplicated (idempotency, e.g. an outbox redelivery).
/// </summary>
public sealed class EmployeeHiredDocumentRequirementHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2026, 9, 1);

    /// <summary>No seeded Contract.* rows: the materialization falls back to the statutory deadline.</summary>
    private static ILegislationParameterReader Legislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(null));
        return reader;
    }

    /// <summary>No open assets by default; AssetHandoverForm-specific behavior is covered by AssetEventDocumentRequirementHandlerTests.</summary>
    private static IAssetLookup AssetLookup()
    {
        var lookup = Substitute.For<IAssetLookup>();
        lookup.HasOpenAssetsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        return lookup;
    }

    /// <summary>No active incentive by default; HasIncentive-specific behavior is covered by IncentiveDocumentRequirementTests.</summary>
    private static IEmployeeIncentiveLookup IncentiveLookup()
    {
        var lookup = Substitute.For<IEmployeeIncentiveLookup>();
        lookup.HasActiveIncentiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(false);
        return lookup;
    }

    private static DocumentTypeDefinition Onboarding(string code) =>
        DocumentTypeDefinition.Create(TenantId, code, code, DocumentTypeStage.Onboarding, true, null, null, 1, false, null, true, 0).Value;

    private static DocumentTypeDefinition Conditional(string code, DocumentRequirementCondition condition) =>
        DocumentTypeDefinition.Create(TenantId, code, code, DocumentTypeStage.Conditional, true, condition, null, 1, false, null, true, 0).Value;

    [Fact]
    public async Task Handle_MaterializesOnboardingAndMatchingConditional_SkipsNonMatchingConditional_AndSkipsAlreadyExisting()
    {
        var alreadyMaterialized = Onboarding("SgkHireDeclaration"); // simulates an outbox redelivery: this one already has a requirement row
        var newOnboarding = Onboarding("NationalIdCopy");
        var matchingConditional = Conditional("ParentalConsent", DocumentRequirementCondition.AgeUnder18);
        var nonMatchingConditional = Conditional("DriverLicenseCopy", DocumentRequirementCondition.RequiresDriving);

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true); // already seeded - EnsureSeededAsync is a no-op
        types.ListAsync(null, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([alreadyMaterialized, newOnboarding, matchingConditional, nonMatchingConditional]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        var existing = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, alreadyMaterialized.Id, null).Value;
        requirements.FindAsync(EmployeeId, alreadyMaterialized.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(existing));
        requirements.FindAsync(EmployeeId, newOnboarding.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));
        requirements.FindAsync(EmployeeId, matchingConditional.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(new EmployeeDocumentEligibilitySignals(IsUnder18: true, HasDisability: false, IsUndergroundWorker: false)));

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, AssetLookup(), IncentiveLookup(), provisioning, Legislation());
        var unitOfWork = Substitute.For<Sense.Modules.Documents.Application.IDocumentsUnitOfWork>();
        var handler = new EmployeeHiredDocumentRequirementHandler(materialization, unitOfWork);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        // Only the two new, applicable types are materialized: newOnboarding (Onboarding) and matchingConditional (AgeUnder18 matches).
        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == newOnboarding.Id));
        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == matchingConditional.Id));
        requirements.DidNotReceive().Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == alreadyMaterialized.Id));
        requirements.DidNotReceive().Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == nonMatchingConditional.Id));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoSignalsForUnknownEmployee_StillMaterializesOnboardingOnly()
    {
        var onboarding = Onboarding("NationalIdCopy");
        var conditional = Conditional("ParentalConsent", DocumentRequirementCondition.AgeUnder18);

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([onboarding, conditional]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.FindAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(null));

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, AssetLookup(), IncentiveLookup(), provisioning, Legislation());
        var unitOfWork = Substitute.For<Sense.Modules.Documents.Application.IDocumentsUnitOfWork>();
        var handler = new EmployeeHiredDocumentRequirementHandler(materialization, unitOfWork);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == onboarding.Id));
        requirements.DidNotReceive().Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == conditional.Id));
    }

    /// <summary>
    /// The written contract gets the statutory two-month deadline (4857 md.8) instead of the generic grace period,
    /// so "sözleşme belgesi yok" surfaces on the right date (Faz 1a #45).
    /// </summary>
    [Fact]
    public async Task Handle_EmploymentContract_UsesTheStatutoryDeadlineInsteadOfTheGracePeriod()
    {
        var contract = Onboarding(DocumentTypeSeed.EmploymentContractCode);
        var otherDocument = Onboarding("NationalIdCopy");

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([contract, otherDocument]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.FindAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(null));

        var materialization = new DocumentRequirementMaterializationService(
            types, requirements, eligibility, AssetLookup(), IncentiveLookup(), new DocumentTypeProvisioningService(types), Legislation());
        var unitOfWork = Substitute.For<Sense.Modules.Documents.Application.IDocumentsUnitOfWork>();
        var handler = new EmployeeHiredDocumentRequirementHandler(materialization, unitOfWork);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r =>
            r.DocumentTypeId == contract.Id
            && r.DueDate == HireDate.AddDays(DocumentRequirementMaterializationService.DefaultContractDocumentDeadlineDays)));
        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r =>
            r.DocumentTypeId == otherDocument.Id
            && r.DueDate == HireDate.AddDays(DocumentRequirementMaterializationService.DefaultGraceDays)));
    }
}
