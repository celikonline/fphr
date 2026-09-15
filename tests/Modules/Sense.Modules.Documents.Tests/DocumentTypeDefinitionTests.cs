using Sense.Modules.Documents.Domain;
using Sense.Modules.Documents.Domain.DocumentTypes;
using Shouldly;
using Xunit;

namespace Sense.Modules.Documents.Tests;

public sealed class DocumentTypeDefinitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static DocumentTypeDefinition Onboarding(string code = "NationalIdCopy") =>
        DocumentTypeDefinition.Create(
            TenantId, code, "Nüfus cüzdanı fotokopisi", DocumentTypeStage.Onboarding, isRequired: true, condition: null,
            renewalPeriodDays: null, quantity: 1, isSensitive: false, defaultRetentionDays: null, isSystem: true, sortOrder: 10).Value;

    [Fact]
    public void Create_Onboarding_Succeeds()
    {
        var result = DocumentTypeDefinition.Create(
            TenantId, "NationalIdCopy", "Nüfus cüzdanı fotokopisi", DocumentTypeStage.Onboarding, true, null, null, 1, false, null, true, 10);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Code.ShouldBe("NationalIdCopy");
        result.Value.Stage.ShouldBe(DocumentTypeStage.Onboarding);
        result.Value.Condition.ShouldBeNull();
        result.Value.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Create_ConditionalWithoutCondition_Fails() =>
        DocumentTypeDefinition.Create(TenantId, "ParentalConsent", "Ebeveyn izni", DocumentTypeStage.Conditional, true, null, null, 1, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeConditionRequiredForConditional);

    [Fact]
    public void Create_ConditionalWithCondition_Succeeds()
    {
        var result = DocumentTypeDefinition.Create(
            TenantId, "ParentalConsent", "Ebeveyn izni", DocumentTypeStage.Conditional, true,
            DocumentRequirementCondition.AgeUnder18, null, 1, false, null, true, 10);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Condition.ShouldBe(DocumentRequirementCondition.AgeUnder18);
    }

    [Theory]
    [InlineData(DocumentTypeStage.Onboarding)]
    [InlineData(DocumentTypeStage.DuringEmployment)]
    [InlineData(DocumentTypeStage.Offboarding)]
    [InlineData(DocumentTypeStage.Recommended)]
    public void Create_NonConditionalWithCondition_Fails(DocumentTypeStage stage) =>
        DocumentTypeDefinition.Create(TenantId, "X", "X", stage, true, DocumentRequirementCondition.AgeUnder18, null, 1, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeConditionNotAllowed);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_CodeRequired_Fails(string? code) =>
        DocumentTypeDefinition.Create(TenantId, code!, "Name", DocumentTypeStage.Onboarding, true, null, null, 1, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeCodeRequired);

    [Fact]
    public void Create_CodeTooLong_Fails() =>
        DocumentTypeDefinition.Create(
                TenantId, new string('a', DocumentsLimits.DocumentTypeCodeMaxLength + 1), "Name", DocumentTypeStage.Onboarding, true, null, null, 1, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeCodeTooLong);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_NameRequired_Fails(string? name) =>
        DocumentTypeDefinition.Create(TenantId, "Code", name!, DocumentTypeStage.Onboarding, true, null, null, 1, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeNameRequired);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_InvalidQuantity_Fails(int quantity) =>
        DocumentTypeDefinition.Create(TenantId, "Code", "Name", DocumentTypeStage.Onboarding, true, null, null, quantity, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeInvalidQuantity);

    [Fact]
    public void Create_InvalidRenewalPeriod_Fails() =>
        DocumentTypeDefinition.Create(TenantId, "Code", "Name", DocumentTypeStage.Onboarding, true, null, 0, 1, false, null, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeInvalidRenewalPeriod);

    [Fact]
    public void Create_InvalidRetention_Fails() =>
        DocumentTypeDefinition.Create(TenantId, "Code", "Name", DocumentTypeStage.Onboarding, true, null, null, 1, false, 0, true, 10)
            .Error.Code.ShouldBe(DocumentsDomainErrors.DocumentTypeInvalidRetention);

    [Fact]
    public void Update_ChangesEditableFieldsOnly_CodeStageConditionIsSystemStayImmutable()
    {
        var type = Onboarding();

        var updated = type.Update("Yeni ad", isRequired: false, renewalPeriodDays: 30, quantity: 2, isSensitive: true, defaultRetentionDays: 90, sortOrder: 99);

        updated.IsSuccess.ShouldBeTrue();
        type.Name.ShouldBe("Yeni ad");
        type.IsRequired.ShouldBeFalse();
        type.RenewalPeriodDays.ShouldBe(30);
        type.Quantity.ShouldBe(2);
        type.IsSensitive.ShouldBeTrue();
        type.DefaultRetentionDays.ShouldBe(90);
        type.SortOrder.ShouldBe(99);

        // Immutable fields untouched.
        type.Code.ShouldBe("NationalIdCopy");
        type.Stage.ShouldBe(DocumentTypeStage.Onboarding);
        type.Condition.ShouldBeNull();
        type.IsSystem.ShouldBeTrue();
    }

    [Fact]
    public void ActivateDeactivate_TogglesIsActive()
    {
        var type = Onboarding();

        type.Deactivate();
        type.IsActive.ShouldBeFalse();

        type.Activate();
        type.IsActive.ShouldBeTrue();
    }
}
