using Sense.Modules.Performance.Domain.ReviewCycles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>Yetkinlik + Hedef: AddGoal → ApproveGoals → EnterGoalResult → ApproveGoalResults, and its gating effect on completion.</summary>
public sealed class GoalFlowTests
{
    private static readonly List<GoalScaleRange> Scale =
    [
        new(0m, 49m, 40m),
        new(50m, 79m, 70m),
        new(80m, 100m, 100m),
    ];

    [Fact]
    public void FullGoalFlow_ThenAssignmentsComplete_CompletesParticipantWithBlendedFinalScore()
    {
        var cycle = ReviewCycleTests.CreateValidCycle(
            ReviewCycleTests.SelfOnlyConfig(), ReviewKind.CompetencyAndGoal, GoalEntryBy.Employee, competencyWeight: 40m);
        var employeeId = Guid.NewGuid();
        cycle.AddParticipants([new ParticipantInput(employeeId, true, new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>>())]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var participant = cycle.Participants.Single();
        var addResult = cycle.AddGoal(participant.Id, Guid.NewGuid(), "Satış hedefi", "Q1 satış", 100m, Scale, null, employeeId);
        addResult.IsSuccess.ShouldBeTrue();

        cycle.ApproveGoals(participant.Id, Guid.NewGuid()).IsSuccess.ShouldBeTrue();
        participant.Goals.Single().Status.ShouldBe(GoalStatus.Approved);

        cycle.EnterGoalResult(addResult.Value, 85m, "Hedefi aştı", employeeId).IsSuccess.ShouldBeTrue();
        participant.Goals.Single().Status.ShouldBe(GoalStatus.ResultEntered);
        participant.Goals.Single().Points.ShouldBe(100m);

        // Goal side is fully approved before the competency form is filled - should not complete the participant yet.
        cycle.ApproveGoalResults(participant.Id, Guid.NewGuid(), DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        participant.Status.ShouldNotBe(ReviewParticipantStatus.Completed);

        var questionId = cycle.Competencies.Single().Questions.Single().Id;
        var selfAssignment = participant.Assignments.Single();
        cycle.SubmitResponse(selfAssignment.Id, employeeId, [(questionId, 60m, null)], DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        participant.Status.ShouldBe(ReviewParticipantStatus.Completed);
        participant.CompetencyScore.ShouldBe(60m);
        participant.GoalScore.ShouldBe(100m);
        participant.GoalProgress.ShouldBe(100m);
        // (40 * 60 + 60 * 100) / 100 = 84.00
        participant.FinalScore.ShouldBe(84.00m);
        cycle.Status.ShouldBe(ReviewCycleStatus.Completed);
    }

    [Fact]
    public void ForceComplete_GoalsNotResultApproved_ParticipantIsIneligible()
    {
        var cycle = ReviewCycleTests.CreateValidCycle(ReviewCycleTests.SelfOnlyConfig(), ReviewKind.CompetencyAndGoal);
        var employeeId = Guid.NewGuid();
        cycle.AddParticipants([new ParticipantInput(employeeId, true, new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>>())]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var participant = cycle.Participants.Single();
        var questionId = cycle.Competencies.Single().Questions.Single().Id;
        var selfAssignment = participant.Assignments.Single();

        // Competency side complete, but no goal ever added/approved for this IncludeGoals participant.
        cycle.SubmitResponse(selfAssignment.Id, employeeId, [(questionId, 90m, null)], DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        participant.Status.ShouldNotBe(ReviewParticipantStatus.Completed);

        var result = cycle.ForceComplete(DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.ForceCompleteNoEligible);
    }

    [Fact]
    public void EnterGoalResult_ActualValueOutsideScale_IsRejected()
    {
        var cycle = ReviewCycleTests.CreateValidCycle(ReviewCycleTests.SelfOnlyConfig(), ReviewKind.CompetencyAndGoal);
        var employeeId = Guid.NewGuid();
        cycle.AddParticipants([new ParticipantInput(employeeId, true, new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>>())]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var participant = cycle.Participants.Single();
        var addResult = cycle.AddGoal(participant.Id, Guid.NewGuid(), "Satış hedefi", null, 100m, Scale, null, employeeId);
        cycle.ApproveGoals(participant.Id, Guid.NewGuid()).IsSuccess.ShouldBeTrue();

        var result = cycle.EnterGoalResult(addResult.Value, 150m, null, employeeId);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.GoalResultOutOfScale);
    }

    [Fact]
    public void EnterGoalResult_BeforeApproval_IsRejected()
    {
        var cycle = ReviewCycleTests.CreateValidCycle(ReviewCycleTests.SelfOnlyConfig(), ReviewKind.CompetencyAndGoal);
        var employeeId = Guid.NewGuid();
        cycle.AddParticipants([new ParticipantInput(employeeId, true, new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>>())]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var participant = cycle.Participants.Single();
        var addResult = cycle.AddGoal(participant.Id, Guid.NewGuid(), "Satış hedefi", null, 100m, Scale, null, employeeId);

        var result = cycle.EnterGoalResult(addResult.Value, 85m, null, employeeId);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.GoalNotApproved);
    }
}
