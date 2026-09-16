using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.Goals;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

public sealed class GoalTests
{
    private static readonly DateOnly TargetDate = new(2026, 12, 31);

    [Fact]
    public void Create_BlankTitle_IsRejected()
    {
        var result = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "   ", null, TargetDate, null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.GoalTitleRequired);
    }

    [Fact]
    public void Create_Valid_StartsOpenAtZeroProgress()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", "Q1 hedefi", TargetDate, null).Value;

        goal.Status.ShouldBe(GoalStatus.Open);
        goal.Progress.ShouldBe(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void UpdateProgress_OutOfRange_IsRejected(int percent)
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;

        var result = goal.UpdateProgress(percent);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.GoalProgressInvalid);
    }

    [Fact]
    public void UpdateProgress_PartialPercent_MovesToInProgress()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;

        goal.UpdateProgress(50);

        goal.Status.ShouldBe(GoalStatus.InProgress);
        goal.Progress.ShouldBe(50);
    }

    [Fact]
    public void UpdateProgress_100Percent_CompletesTheGoal()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;

        goal.UpdateProgress(100);

        goal.Status.ShouldBe(GoalStatus.Completed);
    }

    [Fact]
    public void UpdateProgress_AfterCompletion_IsRejected()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;
        goal.UpdateProgress(100);

        var result = goal.UpdateProgress(50);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.GoalNotUpdatable);
    }

    [Fact]
    public void Cancel_OpenGoal_Succeeds()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;

        var result = goal.Cancel();

        result.IsSuccess.ShouldBeTrue();
        goal.Status.ShouldBe(GoalStatus.Cancelled);
    }

    [Fact]
    public void Cancel_CompletedGoal_IsRejected()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;
        goal.UpdateProgress(100);

        var result = goal.Cancel();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.GoalNotCancellable);
    }

    [Fact]
    public void Archive_OpenGoal_FreezesIt()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;

        goal.Archive();

        goal.Status.ShouldBe(GoalStatus.Archived);
    }

    [Fact]
    public void Archive_CompletedGoal_IsNoOp()
    {
        var goal = Goal.Create(Guid.NewGuid(), Guid.NewGuid(), "Satışları %10 artır", null, TargetDate, null).Value;
        goal.UpdateProgress(100);

        goal.Archive();

        goal.Status.ShouldBe(GoalStatus.Completed);
    }
}
