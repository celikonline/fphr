using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class RecruitmentFormTemplateTests
{
    private static FormFieldInput ScoreField(int weight) => new(null, "Technical skills", FormFieldType.ScoreCard, true, false, null, weight);

    [Fact]
    public void Create_NoFields_IsRejected()
    {
        var result = RecruitmentFormTemplate.Create(Guid.NewGuid(), "Interview Feedback", RecruitmentFormTemplateType.Feedback, []);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.FormTemplateFieldsRequired);
    }

    [Fact]
    public void Create_DropdownFieldWithoutOptions_IsRejected()
    {
        var fields = new[] { new FormFieldInput(null, "Decision", FormFieldType.Dropdown, true, false, [], null) };

        var result = RecruitmentFormTemplate.Create(Guid.NewGuid(), "Outcome", RecruitmentFormTemplateType.Outcome, fields);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.FormFieldOptionsRequired);
    }

    [Fact]
    public void Create_ScorecardWithoutScoreFields_IsRejected()
    {
        var fields = new[] { new FormFieldInput(null, "Notes", FormFieldType.Paragraph, false, false, null, null) };

        var result = RecruitmentFormTemplate.Create(Guid.NewGuid(), "Scorecard", RecruitmentFormTemplateType.Scorecard, fields);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.ScorecardRequiresScoreFields);
    }

    [Fact]
    public void Create_ScorecardWeightsNotSumming100_IsRejected()
    {
        var fields = new[] { ScoreField(50), ScoreField(30) };

        var result = RecruitmentFormTemplate.Create(Guid.NewGuid(), "Scorecard", RecruitmentFormTemplateType.Scorecard, fields);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.ScorecardWeightsMustSumTo100);
    }

    [Fact]
    public void Create_ScorecardWeightsSummingTo100_Succeeds()
    {
        var fields = new[] { ScoreField(60), ScoreField(40) };

        var result = RecruitmentFormTemplate.Create(Guid.NewGuid(), "Scorecard", RecruitmentFormTemplateType.Scorecard, fields);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Fields.Count.ShouldBe(2);
    }

    [Fact]
    public void ReplaceFields_KeepsExplicitIds_AndAssignsNewOnesForNewFields()
    {
        var template = RecruitmentFormTemplate.Create(
            Guid.NewGuid(), "Feedback", RecruitmentFormTemplateType.Feedback,
            [new FormFieldInput(null, "Notes", FormFieldType.Paragraph, false, false, null, null)]).Value;
        var existingId = template.Fields[0].Id;

        var replaced = template.ReplaceFields(
            [
                new FormFieldInput(existingId, "Notes (renamed)", FormFieldType.Paragraph, false, false, null, null),
                new FormFieldInput(null, "Recommend?", FormFieldType.Checkbox, true, false, null, null),
            ]);

        replaced.IsSuccess.ShouldBeTrue();
        template.Fields.Count.ShouldBe(2);
        template.Fields[0].Id.ShouldBe(existingId);
        template.Fields[1].Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Deactivate_Twice_IsRejected()
    {
        var template = RecruitmentFormTemplate.Create(
            Guid.NewGuid(), "Feedback", RecruitmentFormTemplateType.Feedback,
            [new FormFieldInput(null, "Notes", FormFieldType.Paragraph, false, false, null, null)]).Value;
        template.Deactivate();

        var result = template.Deactivate();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.FormTemplateAlreadyInactive);
    }
}
