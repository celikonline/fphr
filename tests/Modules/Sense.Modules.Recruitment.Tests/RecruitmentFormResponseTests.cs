using Sense.Modules.Recruitment.Domain;
using Sense.Modules.Recruitment.Domain.FormResponses;
using Sense.Modules.Recruitment.Domain.FormTemplates;
using Shouldly;
using Xunit;

namespace Sense.Modules.Recruitment.Tests;

public sealed class RecruitmentFormResponseTests
{
    private static RecruitmentFormTemplate FeedbackTemplate() =>
        RecruitmentFormTemplate.Create(
            Guid.NewGuid(), "Interview Feedback", RecruitmentFormTemplateType.Feedback,
            [new FormFieldInput(null, "Notes", FormFieldType.Paragraph, true, false, null, null)]).Value;

    private static RecruitmentFormTemplate ScorecardTemplate() =>
        RecruitmentFormTemplate.Create(
            Guid.NewGuid(), "Technical Scorecard", RecruitmentFormTemplateType.Scorecard,
            [
                new FormFieldInput(null, "Coding", FormFieldType.ScoreCard, true, false, null, 70),
                new FormFieldInput(null, "Communication", FormFieldType.ScoreCard, true, false, null, 30),
            ]).Value;

    [Fact]
    public void Submit_MissingRequiredField_IsRejected()
    {
        var template = FeedbackTemplate();

        var result = RecruitmentFormResponse.Submit(Guid.NewGuid(), template, Guid.NewGuid(), Guid.NewGuid(), new Dictionary<Guid, string?>());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.FormResponseFieldRequired);
    }

    [Fact]
    public void Submit_AllRequiredFieldsAnswered_Succeeds()
    {
        var template = FeedbackTemplate();
        var answers = new Dictionary<Guid, string?> { [template.Fields[0].Id] = "Strong candidate." };

        var result = RecruitmentFormResponse.Submit(Guid.NewGuid(), template, Guid.NewGuid(), Guid.NewGuid(), answers);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Answers.Count.ShouldBe(1);
        result.Value.WeightedScore.ShouldBeNull();
    }

    [Fact]
    public void Submit_ScoreOutOfRange_IsRejected()
    {
        var template = ScorecardTemplate();
        var answers = new Dictionary<Guid, string?> { [template.Fields[0].Id] = "150", [template.Fields[1].Id] = "80" };

        var result = RecruitmentFormResponse.Submit(Guid.NewGuid(), template, Guid.NewGuid(), Guid.NewGuid(), answers);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(RecruitmentDomainErrors.FormResponseScoreInvalid);
    }

    [Fact]
    public void Submit_Scorecard_ComputesWeightedAverage()
    {
        var template = ScorecardTemplate();
        var answers = new Dictionary<Guid, string?> { [template.Fields[0].Id] = "80", [template.Fields[1].Id] = "60" };

        var result = RecruitmentFormResponse.Submit(Guid.NewGuid(), template, Guid.NewGuid(), Guid.NewGuid(), answers);

        result.IsSuccess.ShouldBeTrue();
        // 80*0.7 + 60*0.3 = 56 + 18 = 74
        result.Value.WeightedScore.ShouldBe(74m);
    }
}
