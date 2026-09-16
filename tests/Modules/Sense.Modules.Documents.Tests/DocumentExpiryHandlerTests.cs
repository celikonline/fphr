using NSubstitute;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Employee.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Paging;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>
/// <see cref="GetExpiringDocumentsHandler"/> (GET /api/v1/documents/expiring, S-6): mocks the repository/lookup ports
/// with NSubstitute, same pattern as <c>DocumentTypeHandlerTests</c> - this module has no Testcontainers fixture yet
/// (see final report). A real Postgres round-trip of <c>IEmployeeDocumentRequirementRepository.ListExpiringAsync</c>'s
/// join isn't possible in this pass either: the ExpiryDate column only exists once the AddDocumentExpiry migration
/// (status/faz-04/MIGRATION-ISTEGI.md) is generated and applied.
/// </summary>
public sealed class DocumentExpiryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DocumentTypeId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Guid RequirementId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    private static DocumentTypeDefinition HealthReportType() =>
        DocumentTypeDefinition.Create(
            TenantId, "HealthReport", "Sağlık raporu", DocumentTypeStage.DuringEmployment, true,
            null, null, 1, false, null, true, 50).Value;

    [Fact]
    public async Task Handle_MapsRowToDto_WithEmployeeNameTypeNameAndDaysLeft()
    {
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
        var expiryDate = today.AddDays(10);

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements
            .ListExpiringAsync(Arg.Any<DateOnly>(), 30, null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(
                ([new ExpiringDocumentRow(RequirementId, DocumentId, EmployeeId, DocumentTypeId, expiryDate)], 1)));

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(DocumentTypeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(HealthReportType())));

        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([new EmployeeContact(EmployeeId, "Ayşe", "Yılmaz", "ayse@example.com", null, null, "active")]));

        var handler = new GetExpiringDocumentsHandler(requirements, types, contacts, Tenant(), TimeProvider.System);

        var result = await handler.Handle(new GetExpiringDocumentsQuery(30, null, new PagedQuery()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var item = result.Value.Items.ShouldHaveSingleItem();
        item.DocumentId.ShouldBe(DocumentId);
        item.EmployeeId.ShouldBe(EmployeeId);
        item.EmployeeName.ShouldBe("Ayşe Yılmaz");
        item.DocumentTypeId.ShouldBe(DocumentTypeId);
        item.DocumentTypeName.ShouldBe("Sağlık raporu");
        item.ExpiryDate.ShouldBe(expiryDate);
        item.DaysLeft.ShouldBe(10);
    }

    [Fact]
    public async Task Handle_NonPositiveWithinDays_FallsBackToDefaultWindow()
    {
        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements
            .ListExpiringAsync(Arg.Any<DateOnly>(), DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expired, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(([], 0)));

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        var contacts = Substitute.For<IEmployeeContactLookup>();

        var handler = new GetExpiringDocumentsHandler(requirements, types, contacts, Tenant(), TimeProvider.System);

        var result = await handler.Handle(new GetExpiringDocumentsQuery(0, DocumentExpiryState.Expired, new PagedQuery()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBeEmpty();
        await requirements.Received(1).ListExpiringAsync(
            Arg.Any<DateOnly>(), DocumentsLimits.DefaultExpiryReportWindowDays, DocumentExpiryState.Expired, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RowWithUnknownDocumentType_IsSkipped()
    {
        var today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements
            .ListExpiringAsync(Arg.Any<DateOnly>(), 30, null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<ExpiringDocumentRow> Items, int Total)>(
                ([new ExpiringDocumentRow(RequirementId, DocumentId, EmployeeId, DocumentTypeId, today)], 1)));

        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(DocumentTypeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<DocumentTypeDefinition>(Error.NotFound("document_type.not_found"))));

        var contacts = Substitute.For<IEmployeeContactLookup>();
        contacts.GetContactsAsync(TenantId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmployeeContact>>([]));

        var handler = new GetExpiringDocumentsHandler(requirements, types, contacts, Tenant(), TimeProvider.System);

        var result = await handler.Handle(new GetExpiringDocumentsQuery(30, null, new PagedQuery()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBeEmpty();
    }
}
