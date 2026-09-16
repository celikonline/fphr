using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.DocumentTypes;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class EmployeeDocumentRequirementHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private static DocumentTypeDefinition HealthReportType() =>
        DocumentTypeDefinition.Create(TenantId, "HealthReport", "Sağlık raporu", DocumentTypeStage.Onboarding, true, null, null, 1, true, null, true, 40).Value;

    [Fact]
    public async Task Waive_WithReason_SetsWaivedStatus_AndReturnsDto()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.GetByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(requirement)));
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));
        var currentUser = Substitute.For<ICurrentUser>();
        var waivedBy = Guid.NewGuid();
        currentUser.UserId.Returns(waivedBy);

        var handler = new WaiveDocumentRequirementHandler(currentUser, requirements, types);
        var result = await handler.Handle(new WaiveDocumentRequirementCommand(requirement.Id, "sağlık raporu için muafiyet"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DocumentRequirementStatus.Waived);
        result.Value.WaivedReason.ShouldBe("sağlık raporu için muafiyet");
        result.Value.DocumentTypeCode.ShouldBe("HealthReport");
        requirement.WaivedBy.ShouldBe(waivedBy);
    }

    [Fact]
    public async Task Waive_EmptyReason_Fails_AndLeavesStatusUnchanged()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.GetByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(requirement)));
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        var currentUser = Substitute.For<ICurrentUser>();

        var handler = new WaiveDocumentRequirementHandler(currentUser, requirements, types);
        var result = await handler.Handle(new WaiveDocumentRequirementCommand(requirement.Id, " "), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentsDomainErrors.EmployeeDocumentRequirementWaiveReasonRequired);
        requirement.Status.ShouldBe(DocumentRequirementStatus.Missing);
    }

    [Fact]
    public async Task ClearWaive_ResetsWaivedRequirement_ToMissing()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;
        requirement.Waive("gecici", Guid.NewGuid());

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.GetByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(requirement)));
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));

        var handler = new ClearWaiveHandler(requirements, types);
        var result = await handler.Handle(new ClearWaiveCommand(requirement.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DocumentRequirementStatus.Missing);
        result.Value.WaivedReason.ShouldBeNull();
    }

    [Fact]
    public async Task Link_MarksRequirementUploaded_AndReturnsDto()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;
        var document = Document.Create(TenantId, "saglik-raporu.pdf", "key/saglik-raporu.pdf", "application/pdf", 1024, Guid.NewGuid()).Value;

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.GetByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(requirement)));
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));
        var documents = Substitute.For<IDocumentRepository>();
        documents.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(document)));

        var handler = new LinkDocumentToRequirementHandler(requirements, types, documents);
        var result = await handler.Handle(new LinkDocumentToRequirementCommand(requirement.Id, document.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DocumentRequirementStatus.Uploaded);
        result.Value.DocumentId.ShouldBe(document.Id);
    }

    [Fact]
    public async Task LinkMine_OwnRequirement_MarksUploaded()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;
        var document = Document.Create(TenantId, "saglik-raporu.pdf", "key/saglik-raporu.pdf", "application/pdf", 1024, Guid.NewGuid()).Value;

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.GetByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(requirement)));
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        types.GetByIdAsync(type.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(type)));
        var documents = Substitute.For<IDocumentRepository>();
        documents.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(document)));
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(EmployeeId);

        var handler = new LinkMyDocumentToRequirementHandler(currentUser, requirements, types, documents);
        var result = await handler.Handle(new LinkMyDocumentToRequirementCommand(requirement.Id, document.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DocumentRequirementStatus.Uploaded);
    }

    [Fact]
    public async Task LinkMine_AnotherEmployeesRequirement_Fails_AndLeavesStatusUnchanged()
    {
        var type = HealthReportType();
        var requirement = EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, type.Id, null).Value;
        var document = Document.Create(TenantId, "saglik-raporu.pdf", "key/saglik-raporu.pdf", "application/pdf", 1024, Guid.NewGuid()).Value;

        var requirements = Substitute.For<IEmployeeDocumentRequirementRepository>();
        requirements.GetByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Success(requirement)));
        var types = Substitute.For<IDocumentTypeDefinitionRepository>();
        var documents = Substitute.For<IDocumentRepository>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.EmployeeId.Returns(Guid.NewGuid()); // a different employee than the requirement's owner

        var handler = new LinkMyDocumentToRequirementHandler(currentUser, requirements, types, documents);
        var result = await handler.Handle(new LinkMyDocumentToRequirementCommand(requirement.Id, document.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentsErrors.NotOwner);
        requirement.Status.ShouldBe(DocumentRequirementStatus.Missing);
    }
}
