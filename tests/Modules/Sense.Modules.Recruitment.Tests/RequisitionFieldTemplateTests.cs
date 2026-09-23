using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.JobRequisitions;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class RequisitionFieldTemplateTests
{
    [Fact]
    public void Resolve_NoTemplate_KeepsOriginalFixedBehavior()
    {
        var employmentType = RequisitionFieldTemplate.Resolve(RequisitionFormField.EmploymentType, template: null);
        var reason = RequisitionFieldTemplate.Resolve(RequisitionFormField.Reason, template: null);
        var justification = RequisitionFieldTemplate.Resolve(RequisitionFormField.Justification, template: null);

        employmentType.IsVisible.ShouldBeTrue();
        employmentType.IsRequired.ShouldBeTrue();
        reason.IsVisible.ShouldBeTrue();
        reason.IsRequired.ShouldBeTrue();
        justification.IsVisible.ShouldBeTrue();
        justification.IsRequired.ShouldBeFalse();
    }

    [Fact]
    public void Resolve_TemplateWithoutRuleForAField_FallsBackToDefaultForThatField()
    {
        var template = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: true, DefaultValue: null)]).Value;

        var employmentType = RequisitionFieldTemplate.Resolve(RequisitionFormField.EmploymentType, template);

        employmentType.IsVisible.ShouldBeTrue();
        employmentType.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void Resolve_TemplateMakesJustificationRequired_OverridesDefaultOptionality()
    {
        var template = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: true, DefaultValue: null)]).Value;

        var justification = RequisitionFieldTemplate.Resolve(RequisitionFormField.Justification, template);

        justification.IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void Create_RequiredFieldMarkedHidden_IsRejected()
    {
        var result = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.Reason, IsVisible: false, IsRequired: true, DefaultValue: null)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionFieldTemplateRequiredFieldMustBeVisible);
    }

    [Fact]
    public void Create_OptionalEnumFieldWithoutDefaultValue_IsRejected()
    {
        var result = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.EmploymentType, IsVisible: true, IsRequired: false, DefaultValue: null)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionFieldTemplateDefaultValueRequired);
    }

    [Fact]
    public void Create_OptionalEnumFieldWithInvalidDefaultValue_IsRejected()
    {
        var result = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.Reason, IsVisible: true, IsRequired: false, DefaultValue: "NotARealReason")]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionFieldTemplateDefaultValueRequired);
    }

    [Fact]
    public void Create_OptionalEnumFieldWithValidDefaultValue_Succeeds()
    {
        var result = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.Reason, IsVisible: false, IsRequired: false, DefaultValue: nameof(RequisitionReason.NewPosition))]);

        result.IsSuccess.ShouldBeTrue();
        var reason = RequisitionFieldTemplate.Resolve(RequisitionFormField.Reason, result.Value);
        reason.IsVisible.ShouldBeFalse();
        reason.DefaultValue.ShouldBe(nameof(RequisitionReason.NewPosition));
    }

    [Fact]
    public void Create_DuplicateFieldRule_IsRejected()
    {
        var result = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [
                new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: true, DefaultValue: null),
                new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: false, DefaultValue: null),
            ]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionFieldTemplateDuplicateField);
    }

    [Fact]
    public void Create_EmptyRules_IsRejected()
    {
        var result = RequisitionFieldTemplate.Create(Guid.NewGuid(), Guid.NewGuid(), []);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.RequisitionFieldTemplateRulesRequired);
    }

    [Fact]
    public void ReplaceRules_ValidRules_UpdatesEffectiveResolution()
    {
        var template = RequisitionFieldTemplate.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            [new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: false, DefaultValue: null)]).Value;

        var replaced = template.ReplaceRules(
            [new RequisitionFieldRuleInput(RequisitionFormField.Justification, IsVisible: true, IsRequired: true, DefaultValue: null)]);

        replaced.IsSuccess.ShouldBeTrue();
        RequisitionFieldTemplate.Resolve(RequisitionFormField.Justification, template).IsRequired.ShouldBeTrue();
    }
}
