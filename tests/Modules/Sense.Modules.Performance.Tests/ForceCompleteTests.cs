using Sense.Modules.Performance.Domain.ReviewCycles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>"Değerlendirmeyi tamamla" (ForceComplete): per-category minimum eligibility, Skipped stragglers, and the no-eligible error.</summary>
public sealed class ForceCompleteTests
{
    [Fact]
    public void ForceComplete_ParticipantMeetingMinimum_CompletesAndSkipsStragglers()
    {
        var cycle = ReviewCycleTests.CreateValidCycle(ReviewCycleTests.SelfDisabledManagerOnly(minRequired: 2));

        var employeeId = Guid.NewGuid();
        var evaluatorIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var evaluators = new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>> { [EvaluatorRelation.Manager] = evaluatorIds };
        cycle.AddParticipants([new ParticipantInput(employeeId, false, evaluators)]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var questionId = cycle.Competencies.Single().Questions.Single().Id;
        var participant = cycle.Participants.Single();
        var assignments = participant.Assignments.ToList();

        // Only 2 of the 3 assigned managers respond - meets the MinRequired=2 threshold.
        cycle.SubmitResponse(assignments[0].Id, evaluatorIds[0], [(questionId, 80m, null)], DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        cycle.SubmitResponse(assignments[1].Id, evaluatorIds[1], [(questionId, 90m, null)], DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var now = DateTime.UtcNow;
        var result = cycle.ForceComplete(now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(1);
        participant.Status.ShouldBe(ReviewParticipantStatus.Completed);
        participant.CompetencyScore.ShouldBe(85m); // (80+90)/2, single active relation category → equal-weight branch.
        participant.CompetencyProgress.ShouldBe(100m);
        assignments[2].Status.ShouldBe(ReviewAssignmentStatus.Skipped);
        cycle.Status.ShouldBe(ReviewCycleStatus.Completed);
        cycle.CompletedBy.ShouldBe(ReviewCompletionMode.Forced);
    }

    [Fact]
    public void ForceComplete_NoParticipantMeetsMinimum_ReturnsErrorAndChangesNothing()
    {
        var cycle = ReviewCycleTests.CreateValidCycle(ReviewCycleTests.SelfDisabledManagerOnly(minRequired: 2));

        var employeeId = Guid.NewGuid();
        var evaluators = new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>> { [EvaluatorRelation.Manager] = [Guid.NewGuid(), Guid.NewGuid()] };
        cycle.AddParticipants([new ParticipantInput(employeeId, false, evaluators)]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        // Nobody has responded yet - 0 completed assignments, below MinRequired=2.
        var result = cycle.ForceComplete(DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.ForceCompleteNoEligible);
        cycle.Participants.Single().Status.ShouldBe(ReviewParticipantStatus.Pending);
        cycle.Status.ShouldBe(ReviewCycleStatus.Published);
    }
}
