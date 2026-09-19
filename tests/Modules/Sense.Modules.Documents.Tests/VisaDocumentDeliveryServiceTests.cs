using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sense.Modules.Documents.Application.VisaDocuments;
using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.Documents;
using Sense.Modules.Documents.Domain.VisaDocuments;
using Sense.Modules.Documents.Infrastructure.Persistence.Visa;
using Sense.Shared.Contracts.Storage;
using Sense.Shared.Kernel.Results;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class VisaDocumentDeliveryServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid VisaRequestId = Guid.NewGuid();

    private readonly IDocumentRepository _documents = Substitute.For<IDocumentRepository>();
    private readonly IVisaSettingsProvider _settings = Substitute.For<IVisaSettingsProvider>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();

    private VisaDocumentDeliveryService Service() => new(_documents, _settings, _storage, NullLogger<VisaDocumentDeliveryService>.Instance);

    private static Document ActiveTemplate(string title = "template.pdf")
    {
        var doc = Document.Create(TenantId, title, "templates/template.pdf", "application/pdf", 1024, Guid.NewGuid()).Value;
        doc.ConfirmUpload("etag", 1024);
        return doc;
    }

    [Fact]
    public async Task DeliverVisaDocumentsAsync_WithNoTemplatesConfigured_DeliversNothing()
    {
        _settings.GetTemplateDocumentIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[]);

        var result = await Service().DeliverVisaDocumentsAsync(TenantId, EmployeeId, VisaRequestId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
        _documents.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task DeliverVisaDocumentsAsync_CopiesEachConfiguredTemplateAndAddsADeliveredDocument()
    {
        var template1 = ActiveTemplate("a.pdf");
        var template2 = ActiveTemplate("b.pdf");
        _settings.GetTemplateDocumentIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)[template1.Id, template2.Id]);
        _documents.GetByIdAsync(template1.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(template1));
        _documents.GetByIdAsync(template2.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(template2));

        var result = await Service().DeliverVisaDocumentsAsync(TenantId, EmployeeId, VisaRequestId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
        await _storage.Received(2).CopyObjectAsync(
            TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _documents.Received(2).Add(Arg.Is<Document>(d =>
            d.EmployeeId == EmployeeId && d.VisaDocumentRequestId == VisaRequestId && d.Status == DocumentStatus.Active));
    }

    [Fact]
    public async Task DeliverVisaDocumentsAsync_SkipsATemplateThatNoLongerResolves()
    {
        var missingId = Guid.NewGuid();
        _settings.GetTemplateDocumentIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[missingId]);
        _documents.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Document>(Error.NotFound(DocumentsDomainErrors.DocumentNotFound)));

        var result = await Service().DeliverVisaDocumentsAsync(TenantId, EmployeeId, VisaRequestId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
        await _storage.DidNotReceiveWithAnyArgs().CopyObjectAsync(default, default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task DeliverVisaDocumentsAsync_SkipsATemplateThatIsNotActive()
    {
        var pending = Document.Create(TenantId, "pending.pdf", "templates/pending.pdf", "application/pdf", 1024, Guid.NewGuid()).Value;
        _settings.GetTemplateDocumentIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[pending.Id]);
        _documents.GetByIdAsync(pending.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(pending));

        var result = await Service().DeliverVisaDocumentsAsync(TenantId, EmployeeId, VisaRequestId, CancellationToken.None);

        result.Value.ShouldBe(0);
        _documents.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task DeliverVisaDocumentsAsync_WhenCopyFails_ReturnsFailureAndAddsNothing()
    {
        var template = ActiveTemplate();
        _settings.GetTemplateDocumentIdsAsync(TenantId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<Guid>)[template.Id]);
        _documents.GetByIdAsync(template.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(template));
        _storage.CopyObjectAsync(TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var result = await Service().DeliverVisaDocumentsAsync(TenantId, EmployeeId, VisaRequestId, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(VisaDomainErrors.VisaDocumentDeliveryFailed);
        _documents.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task DeliverVisaDocumentsAsync_WhenALaterCopyFails_AddsNoneOfTheEarlierOnes()
    {
        var first = ActiveTemplate("first.pdf");
        var second = ActiveTemplate("second.pdf");
        _settings.GetTemplateDocumentIdsAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)[first.Id, second.Id]);
        _documents.GetByIdAsync(first.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(first));
        _documents.GetByIdAsync(second.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(second));
        _storage.CopyObjectAsync(TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Is<string>(k => k.EndsWith("second.pdf")), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var result = await Service().DeliverVisaDocumentsAsync(TenantId, EmployeeId, VisaRequestId, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        _documents.DidNotReceiveWithAnyArgs().Add(default!);
    }
}
