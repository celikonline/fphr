using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class EmployeeDocumentRequirementTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DocumentTypeId = Guid.NewGuid();

    private static EmployeeDocumentRequirement Materialize(DateOnly? dueDate = null) =>
        EmployeeDocumentRequirement.Materialize(TenantId, EmployeeId, DocumentTypeId, dueDate).Value;

    [Fact]
    public void Materialize_StartsAsMissing_WithGivenDueDate()
    {
        var dueDate = new DateOnly(2026, 10, 15);
        var requirement = Materialize(dueDate);

        requirement.EmployeeId.ShouldBe(EmployeeId);
        requirement.DocumentTypeId.ShouldBe(DocumentTypeId);
        requirement.Status.ShouldBe(DocumentRequirementStatus.Missing);
        requirement.DueDate.ShouldBe(dueDate);
        requirement.DocumentId.ShouldBeNull();
    }

    [Fact]
    public void MarkUploaded_SetsUploadedStatus_AndClearsAnyPriorWaive()
    {
        var requirement = Materialize();
        var waivedBy = Guid.NewGuid();
        requirement.Waive("gecici muafiyet", waivedBy);

        var documentId = Guid.NewGuid();
        requirement.MarkUploaded(documentId);

        requirement.Status.ShouldBe(DocumentRequirementStatus.Uploaded);
        requirement.DocumentId.ShouldBe(documentId);
        requirement.WaivedReason.ShouldBeNull();
        requirement.WaivedBy.ShouldBeNull();
        requirement.WaivedAt.ShouldBeNull();
    }

    [Fact]
    public void Waive_WithReason_SetsWaivedFields()
    {
        var requirement = Materialize();
        var waivedBy = Guid.NewGuid();

        var result = requirement.Waive("sağlık raporu muaf tutuldu", waivedBy);

        result.IsSuccess.ShouldBeTrue();
        requirement.Status.ShouldBe(DocumentRequirementStatus.Waived);
        requirement.WaivedReason.ShouldBe("sağlık raporu muaf tutuldu");
        requirement.WaivedBy.ShouldBe(waivedBy);
        requirement.WaivedAt.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Waive_WithoutReason_Fails(string? reason)
    {
        var requirement = Materialize();

        var result = requirement.Waive(reason!, Guid.NewGuid());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(DocumentsDomainErrors.EmployeeDocumentRequirementWaiveReasonRequired);
        requirement.Status.ShouldBe(DocumentRequirementStatus.Missing);
    }

    [Fact]
    public void ClearWaive_ResetsToMissing_AndClearsWaiveFields()
    {
        var requirement = Materialize();
        requirement.Waive("sebep", Guid.NewGuid());

        requirement.ClearWaive();

        requirement.Status.ShouldBe(DocumentRequirementStatus.Missing);
        requirement.WaivedReason.ShouldBeNull();
        requirement.WaivedBy.ShouldBeNull();
        requirement.WaivedAt.ShouldBeNull();
    }
}
