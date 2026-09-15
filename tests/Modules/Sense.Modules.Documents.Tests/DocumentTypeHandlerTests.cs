using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Persistence;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

/// <summary>
/// Application-layer handler tests for the document type catalog, mocking the repository/context ports directly
/// with NSubstitute (this module has no existing handler-test project to mirror; the other modules' handler tests
/// use a Testcontainers-backed TestFixture, which is out of scope for this pass - see the final report).
/// </summary>
public sealed class DocumentTypeHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ITenantContext Tenant()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(TenantId);
        return tenant;
    }

    [Fact]
    public async Task Create_NewCode_Succeeds_AndAddsToRepository()
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.CodeExistsAsync("CustomBadge", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new CreateDocumentTypeHandler(Tenant(), types);

        var result = await handler.Handle(
            new CreateDocumentTypeCommand("CustomBadge", "Özel Kart", DocumentTypeStage.Onboarding, true, null, null, 1, false, null, 5),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("CustomBadge");
        result.Value.IsSystem.ShouldBeFalse();
        types.Received(1).Add(Arg.Is<DocumentTypeDefinition>(d => d.Code == "CustomBadge"));
    }

    [Fact]
    public async Task Create_DuplicateCode_FailsWithConflict_AndDoesNotAdd()
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.CodeExistsAsync("NationalIdCopy", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new CreateDocumentTypeHandler(Tenant(), types);

        var result = await handler.Handle(
            new CreateDocumentTypeCommand("NationalIdCopy", "Nüfus cüzdanı", DocumentTypeStage.Onboarding, true, null, null, 1, false, null, 5),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentsErrors.DocumentTypeCodeTaken);
        types.DidNotReceive().Add(Arg.Any<DocumentTypeDefinition>());
    }

    [Fact]
    public async Task Update_ExistingType_ChangesEditableFields()
    {
        var existing = DocumentTypeDefinition.Create(
            TenantId, "HealthReport", "Sağlık raporu", DocumentTypeStage.Onboarding, true, null, null, 1, true, null, true, 40).Value;
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(existing)));
        var handler = new UpdateDocumentTypeHandler(types);

        var result = await handler.Handle(
            new UpdateDocumentTypeCommand(existing.Id, "Sağlık raporu (güncel)", false, 180, 2, true, 365, 41),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Sağlık raporu (güncel)");
        result.Value.IsRequired.ShouldBeFalse();
        result.Value.RenewalPeriodDays.ShouldBe(180);
        result.Value.Quantity.ShouldBe(2);
        result.Value.DefaultRetentionDays.ShouldBe(365);
        result.Value.SortOrder.ShouldBe(41);
        result.Value.Code.ShouldBe("HealthReport");
    }

    [Fact]
    public async Task Update_NotFound_ReturnsError()
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        var missingId = Guid.NewGuid();
        types.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Failure<DocumentTypeDefinition>(Error.NotFound(DocumentsDomainErrors.DocumentTypeNotFound))));
        var handler = new UpdateDocumentTypeHandler(types);

        var result = await handler.Handle(new UpdateDocumentTypeCommand(missingId, "X", true, null, 1, false, null, 0), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeNotFound);
    }

    [Fact]
    public async Task List_UnseededTenant_SeedsCatalog_ThenListsAndSaves()
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(false);
        types.ListAsync(null, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([]));
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var provisioning = new DocumentTypeProvisioningService(types);
        var handler = new ListDocumentTypesHandler(Tenant(), types, provisioning, unitOfWork);

        var result = await handler.Handle(new ListDocumentTypesQuery(null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // Every seed row (43) should have been added via the provisioning service.
        types.Received(DocumentTypeSeed.SystemDefinitions().Count).Add(Arg.Any<DocumentTypeDefinition>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_AlreadySeededTenant_DoesNotReseedOrSave()
    {
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.AnyForTenantAsync(Arg.Any<CancellationToken>()).Returns(true);
        types.ListAsync(null, null, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DocumentTypeDefinition>>([]));
        var unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
        var provisioning = new DocumentTypeProvisioningService(types);
        var handler = new ListDocumentTypesHandler(Tenant(), types, provisioning, unitOfWork);

        var result = await handler.Handle(new ListDocumentTypesQuery(null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        types.DidNotReceive().Add(Arg.Any<DocumentTypeDefinition>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
