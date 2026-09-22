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
/// senseik-009/F-02: closes the internal inconsistency the 2. Tur test findings flagged - the WorkPermit
/// (NonTurkishNational) conditional document type was seeded since Faz4 but could never actually materialize
/// because Employee had no Nationality field. Now that EmployeeDocumentEligibilitySignals.IsForeignNational is
/// wired (DocumentRequirementMaterializationService.MaterializeAsync), this proves WorkPermit materializes for a
/// foreign national employee and stays un-materialized for a Turkish one.
/// </summary>
public sealed class WorkPermitDocumentRequirementTests
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

    private static IEmployeeIncentiveLookup IncentiveLookup()
    {
        var lookup = Substitute.For<IEmployeeIncentiveLookup>();
        lookup.HasActiveIncentiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(false);
        return lookup;
    }

    private static DocumentTypeDefinition WorkPermit() =>
        DocumentTypeDefinition.Create(
            TenantId, "WorkPermit", "Çalışma izni belgesi", DocumentTypeStage.Conditional, true,
            DocumentRequirementCondition.NonTurkishNational, null, 1, false, null, true, 20).Value;

    private static (IDocumentTypeDefinitionRepository Types, IEmployeeDocumentRequirementRepository Requirements, DocumentRequirementMaterializationService Materialization) Build(bool isForeignNational)
    {
        var type = WorkPermit();
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, true, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([type]));

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.FindAsync(EmployeeId, type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmployeeDocumentRequirement?>(null));

        var eligibility = Substitute.For<IEmployeeDocumentEligibilityLookup>();
        eligibility.GetSignalsAsync(TenantId, EmployeeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmployeeDocumentEligibilitySignals?>(
                new EmployeeDocumentEligibilitySignals(IsUnder18: false, HasDisability: false, IsUndergroundWorker: false, IsForeignNational: isForeignNational)));

        var provisioning = new DocumentTypeProvisioningService(types);
        var materialization = new DocumentRequirementMaterializationService(types, requirements, eligibility, AssetLookup(), IncentiveLookup(), provisioning, Legislation(), NSubstitute.Substitute.For<Sense.Modules.OccupationalSafety.Contracts.IOhsProfileLookup>());
        return (types, requirements, materialization);
    }

    [Fact]
    public async Task MaterializeAsync_ForeignNationalEmployee_MaterializesWorkPermit()
    {
        var (_, requirements, materialization) = Build(isForeignNational: true);

        var created = await materialization.MaterializeAsync(TenantId, EmployeeId, HireDate, CancellationToken.None);

        created.ShouldHaveSingleItem();
        requirements.Received(1).Add(Arg.Any<EmployeeDocumentRequirement>());
    }

    [Fact]
    public async Task MaterializeAsync_TurkishEmployee_DoesNotMaterializeWorkPermit()
    {
        var (_, requirements, materialization) = Build(isForeignNational: false);

        var created = await materialization.MaterializeAsync(TenantId, EmployeeId, HireDate, CancellationToken.None);

        created.ShouldBeEmpty();
        requirements.DidNotReceive().Add(Arg.Any<EmployeeDocumentRequirement>());
    }
}
