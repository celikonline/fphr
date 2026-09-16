using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewForms;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

public sealed class ReviewFormTests
{
    [Fact]
    public void Create_BlankTitle_IsRejected()
    {
        var result = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "   ");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.FormTitleRequired);
    }

    [Fact]
    public void SetQuestions_WeightsNotSumming100_IsRejected()
    {
        var form = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "360").Value;

        var result = form.SetQuestions([("İletişim", 40), ("Takım çalışması", 40)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.FormWeightsMustSumTo100);
    }

    [Fact]
    public void SetQuestions_Empty_IsRejected()
    {
        var form = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "360").Value;

        var result = form.SetQuestions([]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.FormQuestionsRequired);
    }

    [Fact]
    public void SetQuestions_ValidWeights_PopulatesOrderedQuestions()
    {
        var form = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "360").Value;

        var result = form.SetQuestions([("İletişim", 60), ("Takım çalışması", 40)]);

        result.IsSuccess.ShouldBeTrue();
        form.Questions.Count.ShouldBe(2);
        form.Questions[0].Order.ShouldBe(0);
        form.Questions[1].Order.ShouldBe(1);
    }

    [Fact]
    public void Publish_WithoutQuestions_IsRejected()
    {
        var form = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "360").Value;

        var result = form.Publish();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.FormQuestionsRequired);
    }

    [Fact]
    public void Publish_ThenSetQuestions_IsRejected()
    {
        var form = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "360").Value;
        form.SetQuestions([("İletişim", 100)]);
        form.Publish();

        var result = form.SetQuestions([("Yeni soru", 100)]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.FormNotEditable);
    }

    [Fact]
    public void Publish_Twice_IsRejected()
    {
        var form = ReviewForm.Create(Guid.NewGuid(), Guid.NewGuid(), "360").Value;
        form.SetQuestions([("İletişim", 100)]);
        form.Publish();

        var result = form.Publish();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.FormNotPublishable);
    }
}
