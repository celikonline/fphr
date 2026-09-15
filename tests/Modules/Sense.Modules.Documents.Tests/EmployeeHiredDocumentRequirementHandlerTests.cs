using NSubstitute;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Contracts.Events;
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
            .Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(new EmployeeDocumentEligibilitySignals(IsUnder18: true, HasDisability: false)));

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, provisioning);
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
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, provisioning);
        var unitOfWork = Substitute.For<Sense.Modules.Documents.Application.IDocumentsUnitOfWork>();
        var handler = new EmployeeHiredDocumentRequirementHandler(materialization, unitOfWork);

        await handler.Handle(new EmployeeHired(TenantId, EmployeeId, HireDate, HireDate), CancellationToken.None);

        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == onboarding.Id));
        requirements.DidNotReceive().Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == conditional.Id));
    }
}
