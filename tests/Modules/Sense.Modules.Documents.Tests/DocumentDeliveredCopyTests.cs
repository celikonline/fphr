using Sense.Modules.Documents.Domain.Documents;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class DocumentDeliveredCopyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid VisaDocumentRequestId = Guid.NewGuid();

    [Fact]
    public void CreateDeliveredCopy_WithValidData_IsActiveAndTaggedWithEmployeeAndSource()
    {
        var result = Document.CreateDeliveredCopy(
            TenantId, EmployeeId, VisaDocumentRequestId, "Vize Basvuru Formu.pdf", "visa-deliveries/req/doc.pdf",
            "application/pdf", 2048, "documents");

        result.IsSuccess.ShouldBeTrue();
        var doc = result.Value;
        doc.Status.ShouldBe(DocumentStatus.Active);
        doc.EmployeeId.ShouldBe(EmployeeId);
        doc.VisaDocumentRequestId.ShouldBe(VisaDocumentRequestId);
        doc.TenantId.ShouldBe(TenantId);
        doc.BucketName.ShouldBe("documents");
    }

    [Fact]
    public void CreateDeliveredCopy_WithEmptyEmployeeId_Fails()
    {
        var result = Document.CreateDeliveredCopy(
            TenantId, Guid.Empty, VisaDocumentRequestId, "title.pdf", "key", "application/pdf", 1024, "documents");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void CreateDeliveredCopy_WithBlankTitle_Fails()
    {
        var result = Document.CreateDeliveredCopy(
            TenantId, EmployeeId, VisaDocumentRequestId, "  ", "key", "application/pdf", 1024, "documents");

        result.IsFailure.ShouldBeTrue();
    }
}
