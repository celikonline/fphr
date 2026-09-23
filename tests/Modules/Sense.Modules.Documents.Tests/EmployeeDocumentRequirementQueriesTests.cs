using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
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

    /// <summary>Faz4 durum takibi #68: "GetMissingDocumentReportQuery bir q parametresi almıyor" - covers the new
    /// optional employee-name filter (matched via <see cref="IEmployeeContactLookup"/>, no direct Employee reference).</summary>
    [Fact]
    public async Task GetMissingDocumentReport_QMatchesOneEmployeeName_ReturnsOnlyThatEmployeesItems()
    {
        var matchingEmployeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var type = HealthReportType();
        var matchingRequirement = EmployeeDocumentRequirement.Materialize(TenantId, matchingEmployeeId, type.Id, null).Value;
        var otherRequirement = EmployeeDocumentRequirement.Materialize(TenantId, otherEmployeeId, type.Id, null).Value;

        var scope = Substitute.For<IDataScopeService>();
        scope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        // Unpaged candidate lookup (skip 0, take int.MaxValue) used to resolve which employees match "q".
        requirements.ListReportAsync(null, 0, int.MaxValue, null, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EmployeeDocumentRequirement>)[matchingRequirement, otherRequirement], 2));
        // Paged lookup, narrowed to the name-matched employee only.
        requirements.ListReportAsync(
                null, 0, 20,
                Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Count == 1 && ids.Contains(matchingEmployeeId)),
                Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EmployeeDocumentRequirement>)[matchingRequirement], 1));

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(
                TenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(matchingEmployeeId) && ids.Contains(otherEmployeeId)),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new EmployeeContact(matchingEmployeeId, "Ayşe", "Yılmaz", null, null, null, "Active"),
                new EmployeeContact(otherEmployeeId, "Mehmet", "Demir", null, null, null, "Active"),
            ]);

        var handler = new GetMissingDocumentReportHandler(requirements, types, contacts, tenant, scope);
        var result = await handler.Handle(new GetMissingDocumentReportQuery(null, new PagedQuery(), "yılmaz"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        var item = result.Value.Items.ShouldHaveSingleItem();
        item.EmployeeId.ShouldBe(matchingEmployeeId);
        item.EmployeeName.ShouldBe("Ayşe Yılmaz");
    }

    [Fact]
    public async Task GetMissingDocumentReport_QBlank_BehaviorUnchanged_ReturnsFullVisibleScope()
    {
        var visible = new HashSet<Guid> { EmployeeId };
        var scope = Substitute.For<IDataScopeService>();
        scope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)visible);
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.ListReportAsync(Arg.Any<DocumentRequirementStatus?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EmployeeDocumentRequirement>)[], 0));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var contacts = Substitute.For<IEmployeeContactLookup>();

        var handler = new GetMissingDocumentReportHandler(requirements, Substitute.For<IDocumentTypeDefinitionRepository>(), contacts, tenant, scope);
        // Q is whitespace-only: must behave exactly like the no-Q case (single ListReportAsync call, visible scope untouched).
        var result = await handler.Handle(new GetMissingDocumentReportQuery(null, new PagedQuery(), "   "), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await requirements.Received(1).ListReportAsync(
            Arg.Any<DocumentRequirementStatus?>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Is<IReadOnlyCollection<Guid>?>(ids => ids != null && ids.Contains(EmployeeId) && ids.Count == 1), Arg.Any<CancellationToken>());
        // Whitespace-only Q must not trigger the name-search path: no unpaged "candidates" ListReportAsync call.
        await requirements.DidNotReceive().ListReportAsync(
            Arg.Any<DocumentRequirementStatus?>(), 0, int.MaxValue, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMissingDocumentReport_QMatchesNoEmployee_ReturnsEmptyResult()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;

        var scope = Substitute.For<IDataScopeService>();
        scope.GetVisibleEmployeeIdsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlySet<Guid>?)null);

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.ListReportAsync(null, 0, int.MaxValue, null, Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<EmployeeDocumentRequirement>)[requirement], 1));

        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(TenantId, Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(EmployeeId)), Arg.Any<CancellationToken>())
            .Returns([new EmployeeContact(EmployeeId, "Mehmet", "Demir", null, null, null, "Active")]);

        var handler = new GetMissingDocumentReportHandler(
            requirements, Substitute.For<IDocumentTypeDefinitionRepository>(), contacts, tenant, scope);
        var result = await handler.Handle(new GetMissingDocumentReportQuery(null, new PagedQuery(), "nomatch-xyz"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(0);
        result.Value.Items.ShouldBeEmpty();
        // No match -> the paged (non-candidate) ListReportAsync call must never happen.
        await requirements.DidNotReceive().ListReportAsync(
            Arg.Any<DocumentRequirementStatus?>(), 0, 20, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>());
    }
}
