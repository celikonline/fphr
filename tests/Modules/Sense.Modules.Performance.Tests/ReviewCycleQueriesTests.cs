using FluentValidation;
using Sense.Modules.Performance.Application.ReviewCycles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

public sealed class ReviewCycleQueriesTests
{
    private readonly ListReviewCyclesValidator validator = new();

    [Fact]
    public void Validate_KnownStatus_IsValid()
    {
        var result = validator.Validate(new ListReviewCyclesQuery("closed"));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_NullStatus_IsValid()
    {
        var result = validator.Validate(new ListReviewCyclesQuery(null));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_UnknownStatus_IsRejected()
    {
        var result = validator.Validate(new ListReviewCyclesQuery("xyz"));

        result.IsValid.ShouldBeFalse();
    }
}
