using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
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

        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new GetEmployeeDocumentRequirementsHandler(requirements, types, documents, scope);
        var result = await handler.Handle(new GetEmployeeDocumentRequirementsQuery(EmployeeId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().ExpiryDate.ShouldBeNull();
        // No document IDs to resolve -> repository should be asked with an empty collection, not skipped silently.
        await documents.Received(1).GetExpiryDatesByIdsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEmployeeDocumentRequirements_EmployeeOutOfScope_IsForbidden_AndReadsNothing()
    {
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        var scope = Substitute.For<IDataScopeService>();
        scope.CanAccessEmployeeAsync(Arg.Any<string>(), EmployeeId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new GetEmployeeDocumentRequirementsHandler(
            requirements, Substitute.For<IDocumentTypeDefinitionRepository>(), Substitute.For<IDocumentRepository>(), scope);
        var result = await handler.Handle(new GetEmployeeDocumentRequirementsQuery(EmployeeId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentsErrors.EmployeeNotInScope);
        await requirements.DidNotReceive().ListByEmployeeAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMissingDocumentReport_PassesVisibleEmployeeScopeToRepository()
    {
        var visible = new HashSet<Guid> { EmployeeId };
        var scope = Substitute.For<IDataScopeService>();
        scope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)visible);
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.ListReportAsync(Arg.Any<DocumentRequirementStatus?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EmployeeDocumentRequirement>)[], 0));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var contacts = Substitute.For<Sense.Modules.Employee.Contracts.IEmployeeContactLookup>();

        var handler = new GetMissingDocumentReportHandler(requirements, Substitute.For<IDocumentTypeDefinitionRepository>(), contacts, tenant, scope);
        var result = await handler.Handle(new GetMissingDocumentReportQuery(null, new Sense.Shared.Contracts.Paging.PagedQuery()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await requirements.Received(1).ListReportAsync(
            Arg.Any<DocumentRequirementStatus?>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Contains(EmployeeId) && ids.Count == 1), Arg.Any<CancellationToken>());
    }
}
