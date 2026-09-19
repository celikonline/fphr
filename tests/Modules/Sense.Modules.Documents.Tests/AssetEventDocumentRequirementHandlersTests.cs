using NSubstitute;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Contracts.Events;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Payroll.Contracts;
using Sense.Shared.Contracts.Legislation;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>
/// AssetAssigned/AssetReturned (Assets module, roadmap #65) → re-runs materialization so the AssetHandoverForm
/// conditional requirement (Condition=HasOpenAssets) tracks the current IAssetLookup.HasOpenAssetsAsync signal.
/// </summary>
public sealed class AssetEventDocumentRequirementHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly IssuedAt = new(2026, 9, 1);

    private static ILegislationParameterReader Legislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(null));
        return reader;
    }

    /// <summary>Mirrors the seed row in DocumentTypeSeed.cs ("AssetHandoverForm", Conditional, HasOpenAssets).</summary>
    private static DocumentTypeDefinition AssetHandoverForm() =>
        DocumentTypeDefinition.Create(
            TenantId, "AssetHandoverForm", "Zimmet formu", DocumentTypeStage.Conditional, true,
            DocumentRequirementCondition.HasOpenAssets, null, 1, false, null, true, 90).Value;

    private (IDocumentTypeDefinitionRepository Types, IEmployeeDocumentRequirementRepository Requirements, IAssetLookup AssetLookup, DocumentRequirementMaterializationService Materialization)
        Build(bool hasOpenAssets)
    {
        var handoverForm = AssetHandoverForm();
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([handoverForm]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.FindAsync(EmployeeId, handoverForm.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(null));

        var assetLookup = Substitute.For<IAssetLookup>();
        assetLookup.HasOpenAssetsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(hasOpenAssets);

        var provisioning = new DocumentTypeProvisioningService(types);
        var incentiveLookup = Substitute.For<IEmployeeIncentiveLookup>();
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, assetLookup, incentiveLookup, provisioning, Legislation());
        return (types, requirements, assetLookup, materialization);
    }

    [Fact]
    public async Task AssetAssignedHandler_WhenEmployeeHasOpenAssets_MaterializesAssetHandoverForm()
    {
        var (_, requirements, _, materialization) = Build(hasOpenAssets: true);
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new AssetAssignedDocumentRequirementHandler(materialization, unitOfWork, TimeProvider.System);

        await handler.Handle(new AssetAssigned(TenantId, Guid.NewGuid(), EmployeeId, IssuedAt), CancellationToken.None);

        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r => r.EmployeeId == EmployeeId));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetReturnedHandler_WhenEmployeeHasNoMoreOpenAssets_DoesNotMaterializeAssetHandoverForm()
    {
        var (_, requirements, _, materialization) = Build(hasOpenAssets: false);
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new AssetReturnedDocumentRequirementHandler(materialization, unitOfWork, TimeProvider.System);

        await handler.Handle(new AssetReturned(TenantId, Guid.NewGuid(), EmployeeId, IssuedAt), CancellationToken.None);

        requirements.DidNotReceive().Add(Arg.Any<EmployeeDocumentRequirement>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssetReturnedHandler_WhenRequirementAlreadyMaterialized_DoesNotDuplicate()
    {
        var handoverForm = AssetHandoverForm();
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([handoverForm]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        var existing = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, handoverForm.Id, null).Value;
        requirements.FindAsync(EmployeeId, handoverForm.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(existing));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(null));

        // The asset was just returned, so HasOpenAssetsAsync is now false - but the requirement was already
        // materialized while it was open, and MaterializeAsync only adds missing rows (append-only, see
        // DocumentRequirementMaterializationService remarks); it stays regardless of the now-false signal.
        var assetLookup = Substitute.For<IAssetLookup>();
        assetLookup.HasOpenAssetsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(false);

        var provisioning = new DocumentTypeProvisioningService(types);
        var incentiveLookup = Substitute.For<IEmployeeIncentiveLookup>();
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, assetLookup, incentiveLookup, provisioning, Legislation());
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var handler = new AssetReturnedDocumentRequirementHandler(materialization, unitOfWork, TimeProvider.System);

        await handler.Handle(new AssetReturned(TenantId, Guid.NewGuid(), EmployeeId, IssuedAt), CancellationToken.None);

        requirements.DidNotReceive().Add(Arg.Any<EmployeeDocumentRequirement>());
    }
}
