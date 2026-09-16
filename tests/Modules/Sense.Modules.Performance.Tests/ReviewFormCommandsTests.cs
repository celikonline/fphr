using NSubstitute;
using Sense.Modules.Performance.Application;
using Sense.Modules.Performance.Application.ReviewForms;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Modules.Performance.Domain.ReviewForms;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>PublishReviewFormHandler's cycle-level immutability guard: 4857 md.18 fesih delili gerekçesi, bir dönem
/// kapatıldıktan sonra o döneme bağlı hiçbir form artık yayınlanamaz - bkz. handler'ın XML doc'u.</summary>
public sealed class ReviewFormCommandsTests
{
    private static ReviewCycle ClosedCycle()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)).Value;
        cycle.Activate();
        cycle.Close();
        return cycle;
    }

    [Fact]
    public async Task Publish_CycleClosed_IsRejected()
    {
        var cycle = ClosedCycle();
        var form = ReviewForm.Create(cycle.TenantId, cycle.Id, "360").Value;
        form.SetQuestions([("İletişim", 100)]);

        var forms = Substitute.For<IReviewFormRepository>();
        forms.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);
        var cycles = Substitute.For<IReviewCycleRepository>();
        cycles.GetByIdAsync(cycle.Id, Arg.Any<CancellationToken>()).Returns(cycle);
        var handler = new PublishReviewFormHandler(forms, cycles);

        var result = await handler.Handle(new PublishReviewFormCommand(form.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceErrors.ReviewImmutable);
        form.Status.ShouldBe(ReviewFormStatus.Draft);
    }

    [Fact]
    public async Task Publish_CycleActive_Succeeds()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q2", new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30)).Value;
        cycle.Activate();
        var form = ReviewForm.Create(cycle.TenantId, cycle.Id, "360").Value;
        form.SetQuestions([("İletişim", 100)]);

        var forms = Substitute.For<IReviewFormRepository>();
        forms.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);
        var cycles = Substitute.For<IReviewCycleRepository>();
        cycles.GetByIdAsync(cycle.Id, Arg.Any<CancellationToken>()).Returns(cycle);
        var handler = new PublishReviewFormHandler(forms, cycles);

        var result = await handler.Handle(new PublishReviewFormCommand(form.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        form.Status.ShouldBe(ReviewFormStatus.Published);
    }
}
