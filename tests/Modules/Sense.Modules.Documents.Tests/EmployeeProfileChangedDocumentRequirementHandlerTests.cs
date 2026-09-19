using NSubstitute;
using Sense.Modules.Assets.Contracts;
using Sense.Modules.Documents.Application;
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
/// EmployeeProfileChanged (Employee module) re-evaluation, and the "append-only materialization sorunu" fix (faz4
/// TB-9) it exercises: a newly-matching Conditional requirement gets materialized, and a stale still-Missing one
/// whose condition stopped matching gets auto-waived instead of dangling forever.
/// </summary>
public sealed class EmployeeProfileChangedDocumentRequirementHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static ILegislationParameterReader Legislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(null));
        return reader;
    }

    private static DocumentTypeDefinition AgeUnder18Type() =>
        DocumentTypeDefinition.Create(
            TenantId, "MinorWorkPermit", "18 yaş altı çalışma izni", DocumentTypeStage.Conditional, true,
            DocumentRequirementCondition.AgeUnder18, null, 1, false, null, true, 50).Value;

    private static (IDocumentTypeDefinitionRepository Types, IEmployeeDocumentRequirementRepository Requirements, DocumentRequirementMaterializationService Materialization) Build(
        DocumentTypeDefinition type, bool isUnder18, IReadOnlyList<EmployeeDocumentRequirement>? existing = null)
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([type]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeDocumentRequirement>>(existing ?? []));
        if (existing is { Count: > 0 } list)
        {
            foreach (var requirement in list)
            {
                requirements.FindAsync(EmployeeId, requirement.DocumentTypeId, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<EmployeeDocumentRequirement?>(requirement));
            }
        }

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(new EmployeeDocumentEligibilitySignals(IsUnder18: isUnder18, HasDisability: false, IsUndergroundWorker: false)));

        var assetLookup = Substitute.For<IAssetLookup>();
        assetLookup.HasOpenAssetsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(false);
        var incentiveLookup = Substitute.For<IEmployeeIncentiveLookup>();

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, assetLookup, incentiveLookup, provisioning, Legislation());
        return (types, requirements, materialization);
    }

    [Fact]
    public async Task MaterializeAsync_WhenConditionNoLongerMatches_AutoWaivesStaleMissingRequirement()
    {
        var type = AgeUnder18Type();
        var existing = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, new DateOnly(2026, 1, 1)).Value;
        var (_, _, materialization) = Build(type, isUnder18: false, existing: [existing]);

        await materialization.MaterializeAsync(TenantId, EmployeeId, new DateOnly(2026, 9, 16), CancellationToken.None);

        existing.Status.ShouldBe(DocumentRequirementStatus.Waived);
        existing.WaivedReason.ShouldBe(DocumentRequirementMaterializationService.AutoWaivedReason);
        existing.WaivedBy.ShouldBe(DocumentRequirementMaterializationService.SystemActorId);
    }

    [Fact]
    public async Task MaterializeAsync_WhenConditionStillMatches_LeavesMissingRequirementAlone()
    {
        var type = AgeUnder18Type();
        var existing = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, new DateOnly(2026, 1, 1)).Value;
        var (_, requirements, materialization) = Build(type, isUnder18: true, existing: [existing]);

        await materialization.MaterializeAsync(TenantId, EmployeeId, new DateOnly(2026, 9, 16), CancellationToken.None);

        existing.Status.ShouldBe(DocumentRequirementStatus.Missing);
        requirements.DidNotReceive().Add(Arg.Any<EmployeeDocumentRequirement>());
    }

    [Fact]
    public async Task MaterializeAsync_DoesNotAutoWaiveAlreadyUploadedRequirement()
    {
        var type = AgeUnder18Type();
        var existing = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, new DateOnly(2026, 1, 1)).Value;
        existing.MarkUploaded(Guid.NewGuid());
        var (_, _, materialization) = Build(type, isUnder18: false, existing: [existing]);

        await materialization.MaterializeAsync(TenantId, EmployeeId, new DateOnly(2026, 9, 16), CancellationToken.None);

        existing.Status.ShouldBe(DocumentRequirementStatus.Uploaded);
        existing.WaivedReason.ShouldBeNull();
    }

    [Fact]
    public async Task MaterializeAsync_DoesNotReWaiveAlreadyManuallyWaivedRequirement()
    {
        var type = AgeUnder18Type();
        var existing = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, new DateOnly(2026, 1, 1)).Value;
        var manualWaiver = Guid.NewGuid();
        existing.Waive("elden takip ediliyor", manualWaiver);
        var (_, _, materialization) = Build(type, isUnder18: false, existing: [existing]);

        await materialization.MaterializeAsync(TenantId, EmployeeId, new DateOnly(2026, 9, 16), CancellationToken.None);

        existing.WaivedBy.ShouldBe(manualWaiver);
        existing.WaivedReason.ShouldBe("elden takip ediliyor");
    }

    [Fact]
    public async Task Handle_RunsMaterializationAndSaves()
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(null));
        var assetLookup = Substitute.For<IAssetLookup>();
        assetLookup.HasOpenAssetsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(false);
        var incentiveLookup = Substitute.For<IEmployeeIncentiveLookup>();

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, assetLookup, incentiveLookup, provisioning, Legislation());
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();

        var handler = new EmployeeProfileChangedDocumentRequirementHandler(materialization, unitOfWork, TimeProvider.System);
        await handler.Handle(new EmployeeProfileChanged(TenantId, EmployeeId, EmployeeProfileSections.SelfService), CancellationToken.None);

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
