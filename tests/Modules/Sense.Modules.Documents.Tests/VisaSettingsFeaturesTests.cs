using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaSettingsFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IVisaSettingsRepository _repository = Substitute.For<IVisaSettingsRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDocumentsUnitOfWork _unitOfWork = Substitute.For<IDocumentsUnitOfWork>();
    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();

    public VisaSettingsFeaturesTests()
    {
        _tenant.TenantId.Returns(TenantId);
    }

    [Fact]
    public async Task Get_WhenNoSettingsExistYet_SeedsAndPersistsADefaultRow()
    {
        _repository.FindAsync(Arg.Any<CancellationToken>()).Returns((VisaSettings?)null);

        var handler = new GetVisaSettingsHandler(_repository, _tenant, _unitOfWork);
        var result = await handler.Handle(new GetVisaSettingsQuery(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ApproverEmployeeId.ShouldBeNull();
        result.Value.TemplateDocumentIds.ShouldBeEmpty();

        _repository.Received(1).Add(Arg.Any<VisaSettings>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_WhenSettingsAlreadyExist_DoesNotReseed()
    {
        var existing = VisaSettings.CreateDefault(TenantId);
        existing.SetApprover(Guid.NewGuid());
        _repository.FindAsync(Arg.Any<CancellationToken>()).Returns(existing);

        var handler = new GetVisaSettingsHandler(_repository, _tenant, _unitOfWork);
        var result = await handler.Handle(new GetVisaSettingsQuery(), CancellationToken.None);

        result.Value.ApproverEmployeeId.ShouldBe(existing.ApproverEmployeeId);
        _repository.DidNotReceiveWithAnyArgs().Add(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Update_SetsApproverAndTemplatesOnTheTenantsSettings()
    {
        var existing = VisaSettings.CreateDefault(TenantId);
        _repository.FindAsync(Arg.Any<CancellationToken>()).Returns(existing);
        var approverId = Guid.NewGuid();
        var template = ActiveCompanyDocument();
        var templateId = template.Id;
        _documents.GetByIdAsync(templateId, Arg.Any<CancellationToken>()).Returns(Result.Success(template));

        var handler = new UpdateVisaSettingsHandler(_repository, _tenant, _unitOfWork, _documents);
        var result = await handler.Handle(new UpdateVisaSettingsCommand(approverId, [templateId]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ApproverEmployeeId.ShouldBe(approverId);
        result.Value.TemplateDocumentIds.ShouldBe([templateId]);
        existing.ApproverEmployeeId.ShouldBe(approverId);
    }

    [Fact]
    public async Task Update_WhenNoSettingsExistYet_SeedsBeforeApplyingChanges()
    {
        _repository.FindAsync(Arg.Any<CancellationToken>()).Returns((VisaSettings?)null);

        var handler = new UpdateVisaSettingsHandler(_repository, _tenant, _unitOfWork, _documents);
        var result = await handler.Handle(new UpdateVisaSettingsCommand(Guid.NewGuid(), []), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Add(Arg.Any<VisaSettings>());
    }

    private static Document ActiveCompanyDocument()
    {
        var doc = Document.Create(TenantId, "template.pdf", "templates/template.pdf", "application/pdf", 1024, Guid.NewGuid()).Value;
        doc.ConfirmUpload("etag", 1024);
        return doc;
    }

    [Fact]
    public async Task Update_WithUnknownTemplateDocument_IsRejectedAndChangesNothing()
    {
        var existing = VisaSettings.CreateDefault(TenantId);
        _repository.FindAsync(Arg.Any<CancellationToken>()).Returns(existing);
        var missingId = Guid.NewGuid();
        _documents.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Document>(Error.NotFound("document.not_found")));

        var result = await new UpdateVisaSettingsHandler(_repository, _tenant, _unitOfWork, _documents)
            .Handle(new UpdateVisaSettingsCommand(Guid.NewGuid(), [missingId]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaSettingsTemplateInvalid);
        existing.TemplateDocumentIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_WithAnEmployeeFolderCopyAsTemplate_IsRejected()
    {
        var existing = VisaSettings.CreateDefault(TenantId);
        _repository.FindAsync(Arg.Any<CancellationToken>()).Returns(existing);
        var delivered = Document.CreateDeliveredCopy(
            TenantId, Guid.NewGuid(), Guid.NewGuid(), "copy.pdf", "visa-deliveries/x/y.pdf", "application/pdf", 10, "documents").Value;
        _documents.GetByIdAsync(delivered.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(delivered));

        var result = await new UpdateVisaSettingsHandler(_repository, _tenant, _unitOfWork, _documents)
            .Handle(new UpdateVisaSettingsCommand(null, [delivered.Id]), CancellationToken.None);

        result.Error.Code.ShouldBe(VisaDomainErrors.VisaSettingsTemplateInvalid);
    }

    [Fact]
    public void UpdateValidator_CapsTheTemplateList()
    {
        var validator = new UpdateVisaSettingsValidator();
        var tooMany = Enumerable.Range(0, UpdateVisaSettingsValidator.MaxTemplateDocuments + 1).Select(_ => Guid.NewGuid()).ToList();

        validator.Validate(new UpdateVisaSettingsCommand(null, tooMany)).IsValid.ShouldBeFalse();
        validator.Validate(new UpdateVisaSettingsCommand(null, tooMany.Take(UpdateVisaSettingsValidator.MaxTemplateDocuments).ToList())).IsValid.ShouldBeTrue();
    }
}
