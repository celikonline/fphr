using NSubstitute;
using Sense.Modules.Assets.Contracts;
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
/// senseik-010/F-03: closes the same kind of internal inconsistency as WorkPermitDocumentRequirementTests, but for
/// the IncentiveDocument ("Teşvik belgesi") conditional type (seeded since Faz4, Condition=HasIncentive) - it could
/// never actually materialize because HasIncentive was hardcoded false. Now that
/// DocumentRequirementMaterializationService.MaterializeAsync reads Payroll.Contracts.IEmployeeIncentiveLookup
/// (backed by EmployeePayrollProfile.IncentiveLawCode), this proves IncentiveDocument materializes for an employee
/// with an active incentive law code and stays un-materialized for one without.
/// </summary>
public sealed class IncentiveDocumentRequirementTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly HireDate = new(2026, 9, 1);

    private static ILegislationParameterReader Legislation()
    {
        var reader = Substitute.For<ILegislationParameterReader>();
        reader.FindAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LegislationParameterValue?>(null));
        return reader;
    }

    private static IAssetLookup AssetLookup()
    {
        var lookup = Substitute.For<IAssetLookup>();
        lookup.HasOpenAssetsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        return lookup;
    }

    /// <summary>Mirrors the seed row in DocumentTypeSeed.cs ("IncentiveDocument", Conditional, HasIncentive, sortOrder 30).</summary>
    private static DocumentTypeDefinition IncentiveDocument() =>
        DocumentTypeDefinition.Create(
            TenantId, "IncentiveDocument", "Teşvik belgesi", DocumentTypeStage.Conditional, true,
            DocumentRequirementCondition.HasIncentive, null, 1, false, null, true, 30).Value;

    private static (IDocumentTypeDefinitionRepository Types, IEmployeeDocumentRequirementRepository Requirements, DocumentRequirementMaterializationService Materialization) Build(bool hasActiveIncentive)
    {
        var type = IncentiveDocument();
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([type]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.FindAsync(EmployeeId, type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(null));

        var incentiveLookup = Substitute.For<IEmployeeIncentiveLookup>();
        incentiveLookup.HasActiveIncentiveAsync(TenantId, EmployeeId, HireDate, Arg.Any<CancellationToken>()).Returns(hasActiveIncentive);

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, AssetLookup(), incentiveLookup, provisioning, Legislation());
        return (types, requirements, materialization);
    }

    [Fact]
    public async Task MaterializeAsync_EmployeeWithActiveIncentive_MaterializesIncentiveDocument()
    {
        var (_, requirements, materialization) = Build(hasActiveIncentive: true);

        var created = await materialization.MaterializeAsync(TenantId, EmployeeId, HireDate, CancellationToken.None);

        created.ShouldHaveSingleItem();
        requirements.Received(1).Add(Arg.Is<EmployeeDocumentRequirement>(r => r.DocumentTypeId == created[0].DocumentTypeId));
    }

    [Fact]
    public async Task MaterializeAsync_EmployeeWithoutActiveIncentive_DoesNotMaterializeIncentiveDocument()
    {
        var (_, requirements, materialization) = Build(hasActiveIncentive: false);

        var created = await materialization.MaterializeAsync(TenantId, EmployeeId, HireDate, CancellationToken.None);

        created.ShouldBeEmpty();
        requirements.DidNotReceive().Add(Arg.Any<EmployeeDocumentRequirement>());
    }
}
