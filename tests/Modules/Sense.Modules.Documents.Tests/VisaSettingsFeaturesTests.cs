using NSubstitute;
using Sense.Modules.Documents.Application;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Shared.Contracts.Context;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaSettingsFeaturesTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IVisaSettingsRepository _repository = Substitute.For<IVisaSettingsRepository>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly IDocumentsUnitOfWork _unitOfWork = Substitute.For<IDocumentsUnitOfWork>();

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
        var templateId = Guid.NewGuid();

        var handler = new UpdateVisaSettingsHandler(_repository, _tenant, _unitOfWork);
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

        var handler = new UpdateVisaSettingsHandler(_repository, _tenant, _unitOfWork);
        var result = await handler.Handle(new UpdateVisaSettingsCommand(Guid.NewGuid(), []), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _repository.Received(1).Add(Arg.Any<VisaSettings>());
    }
}
