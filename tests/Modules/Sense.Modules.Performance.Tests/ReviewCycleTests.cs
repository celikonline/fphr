using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

public sealed class ReviewCycleTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 3, 31);

    [Fact]
    public void Create_BlankName_IsRejected()
    {
        var result = ReviewCycle.Create(Guid.NewGuid(), "   ", Start, End);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.CycleNameRequired);
    }

    [Fact]
    public void Create_EndBeforeStart_IsRejected()
    {
        var result = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", End, Start);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.CycleInvalidDateRange);
    }

    [Fact]
    public void Create_Valid_StartsAsDraft()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", Start, End).Value;

        cycle.Status.ShouldBe(ReviewCycleStatus.Draft);
    }

    [Fact]
    public void Activate_FromDraft_Succeeds()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", Start, End).Value;

        var result = cycle.Activate();

        result.IsSuccess.ShouldBeTrue();
        cycle.Status.ShouldBe(ReviewCycleStatus.Active);
    }

    [Fact]
    public void Activate_Twice_IsRejected()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", Start, End).Value;
        cycle.Activate();

        var result = cycle.Activate();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.CycleNotActivatable);
    }

    [Fact]
    public void Close_WithoutActivating_IsRejected()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", Start, End).Value;

        var result = cycle.Close();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.CycleNotClosable);
    }

    [Fact]
    public void Close_FromActive_IsTerminal()
    {
        var cycle = ReviewCycle.Create(Guid.NewGuid(), "2026 Q1", Start, End).Value;
        cycle.Activate();

        var result = cycle.Close();

        result.IsSuccess.ShouldBeTrue();
        cycle.Status.ShouldBe(ReviewCycleStatus.Closed);

        var second = cycle.Close();
        second.IsFailure.ShouldBeTrue();
        second.Error.Code.ShouldBe(PerformanceDomainErrors.CycleAlreadyClosed);
    }
}
