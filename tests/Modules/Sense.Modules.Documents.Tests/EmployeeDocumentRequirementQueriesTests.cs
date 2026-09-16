using NSubstitute;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>Covers the <c>ExpiryDate</c> field added to <see cref="EmployeeDocumentRequirementDto"/> (faz-04
/// "gereksinim DTO'sunda geçerlilik tarihi"): resolved via a single batched <c>IDocumentRepository</c> call,
/// null for requirements without a linked document.</summary>
public sealed class EmployeeDocumentRequirementQueriesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static DocumentTypeDefinition HealthReportType() =>
        DocumentTypeDefinition.Create(TenantId, "HealthReport", "Sağlık raporu", DocumentTypeStage.Onboarding, true, null, null, 1, true, null, true, 40).Value;

    [Fact]
    public async Task GetMyDocumentRequirements_LinkedDocumentWithExpiry_ReturnsExpiryDate()
    {
        var type = HealthReportType();
        var documentId = Guid.NewGuid();
        var expiryDate = new DateOnly(2027, 3, 1);
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;
        requirement.MarkUploaded(documentId);

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns([requirement]);
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));
        var documents = Substitute.For<IDocumentRepository>();
        documents.GetExpiryDatesByIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(documentId)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, DateOnly?> { [documentId] = expiryDate });
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(EmployeeId);

        var handler = new GetMyDocumentRequirementsHandler(currentUser, requirements, types, documents);
        var result = await handler.Handle(new GetMyDocumentRequirementsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().ExpiryDate.ShouldBe(expiryDate);
    }

    [Fact]
    public async Task GetEmployeeDocumentRequirements_NoLinkedDocument_ExpiryDateIsNull()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.ListByEmployeeAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns([requirement]);
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));
        var documents = Substitute.For<IDocumentRepository>();
        documents.GetExpiryDatesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, DateOnly?>());

        var handler = new GetEmployeeDocumentRequirementsHandler(requirements, types, documents);
        var result = await handler.Handle(new GetEmployeeDocumentRequirementsQuery(EmployeeId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().ExpiryDate.ShouldBeNull();
        // No document IDs to resolve -> repository should be asked with an empty collection, not skipped silently.
        await documents.Received(1).GetExpiryDatesByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }
}
