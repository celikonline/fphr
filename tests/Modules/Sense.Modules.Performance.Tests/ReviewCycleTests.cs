using Sense.Modules.Performance.Domain.ReviewCycles;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>
/// Pure domain-level tests for the wizard creation → participants → publish → fill → natural-completion flow,
/// exercised only through ReviewCycle's public aggregate API (Create/AddParticipants/Publish/SubmitResponse).
/// </summary>
public sealed class ReviewCycleTests
{
    private static readonly DateOnly PeriodStart = new(2026, 1, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 3, 31);

    [Fact]
    public void Create_ValidWizardPayload_Succeeds()
    {
        var result = ReviewCycle.Create(
            Guid.NewGuid(), "2026 Q1", PeriodStart, PeriodEnd, ReviewKind.Competency,
            RatingScale.ZeroToHundred, CommentMode.Optional, EvaluatorLabelMode.Name, reportShowPhoto: true,
            employeeCanViewResults: true, employeeCanDownloadPdf: true, SelfAndManagerConfigs(managerMinRequired: 1), OneCompetencyOneQuestion());

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ReviewCycleStatus.Draft);
    }

    [Fact]
    public void Create_GoalKind_IsRejected()
    {
        var result = ReviewCycle.Create(
            Guid.NewGuid(), "2026 Q1", PeriodStart, PeriodEnd, ReviewKind.CompetencyAndGoal,
            RatingScale.ZeroToHundred, CommentMode.Optional, EvaluatorLabelMode.Name, false, false, false,
            SelfAndManagerConfigs(1), OneCompetencyOneQuestion());

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.CycleGoalKindNotYetSupported);
    }

    /// <summary>
    /// 11.3.10 worked example: "10 çalışanın 7'sine 5 üst, 3'üne 2 üst → üst sınır 2". Publish must accept a
    /// Manager.MinRequired up to the smallest per-participant evaluator count (2) and reject anything higher.
    /// </summary>
    [Fact]
    public void Publish_MinRequiredAtOrBelowSmallestAssignedCount_Succeeds()
    {
        var cycle = CreateValidCycle(SelfDisabledManagerOnly(minRequired: 2));
        AddTenParticipants(cycle, sevenWithFiveManagers: true);

        var result = cycle.Publish(DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        cycle.Status.ShouldBe(ReviewCycleStatus.Published);
    }

    [Fact]
    public void Publish_MinRequiredAboveSmallestAssignedCount_IsRejected()
    {
        var cycle = CreateValidCycle(SelfDisabledManagerOnly(minRequired: 3));
        AddTenParticipants(cycle, sevenWithFiveManagers: true);

        var result = cycle.Publish(DateTime.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(PerformanceDomainErrors.MinEvaluatorsExceedsMax);
        result.Error.Args!["max"].ShouldBe(2);
    }

    [Fact]
    public void SubmitResponse_LastOutstandingAssignment_CompletesParticipantAndCycle()
    {
        var cycle = CreateValidCycle(SelfOnlyConfig());
        var employeeId = Guid.NewGuid();
        var added = cycle.AddParticipants([new ParticipantInput(employeeId, false, new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>>())]);
        added.IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var assignment = cycle.Participants.Single().Assignments.Single();
        var now = DateTime.UtcNow;
        var questionId = cycle.Competencies.Single().Questions.Single().Id;

        var result = cycle.SubmitResponse(assignment.Id, employeeId, [(questionId, 80m, null)], now);

        result.IsSuccess.ShouldBeTrue();
        var participant = cycle.Participants.Single();
        participant.Status.ShouldBe(ReviewParticipantStatus.Completed);
        participant.CompetencyProgress.ShouldBe(100m);
        participant.CompetencyScore.ShouldBe(80m);
        participant.FinalScore.ShouldBe(80m);
        cycle.Status.ShouldBe(ReviewCycleStatus.Completed);
        cycle.CompletedBy.ShouldBe(ReviewCompletionMode.Natural);
    }

    /// <summary>Two relation categories with explicit (non-blank) weights: score must be the weighted average, not a simple average.</summary>
    [Fact]
    public void Score_ExplicitRelationWeights_UsesWeightedAverageNotSimpleAverage()
    {
        var relationConfigs = new List<RelationConfigInput>
        {
            new(EvaluatorRelation.Self, true, 30m, 1),
            new(EvaluatorRelation.Manager, true, 70m, 1),
            new(EvaluatorRelation.Subordinate, false, null, null),
            new(EvaluatorRelation.Peer, false, null, null),
        };
        var cycle = CreateValidCycle(relationConfigs);
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var evaluators = new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>> { [EvaluatorRelation.Manager] = [managerId] };
        cycle.AddParticipants([new ParticipantInput(employeeId, false, evaluators)]).IsSuccess.ShouldBeTrue();
        cycle.Publish(DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        var questionId = cycle.Competencies.Single().Questions.Single().Id;
        var participant = cycle.Participants.Single();
        var selfAssignment = participant.Assignments.Single(a => a.Relation == EvaluatorRelation.Self);
        var managerAssignment = participant.Assignments.Single(a => a.Relation == EvaluatorRelation.Manager);

        cycle.SubmitResponse(selfAssignment.Id, employeeId, [(questionId, 100m, null)], DateTime.UtcNow).IsSuccess.ShouldBeTrue();
        cycle.SubmitResponse(managerAssignment.Id, managerId, [(questionId, 50m, null)], DateTime.UtcNow).IsSuccess.ShouldBeTrue();

        // (30 * 100 + 70 * 50) / 100 = 65.00 - a simple average of 100 and 50 would incorrectly give 75.
        cycle.Participants.Single().CompetencyScore.ShouldBe(65.00m);
    }

    private static ReviewCycle CreateValidCycle(IReadOnlyList<RelationConfigInput> relationConfigs) =>
        ReviewCycle.Create(
            Guid.NewGuid(), "2026 Q1", PeriodStart, PeriodEnd, ReviewKind.Competency,
            RatingScale.ZeroToHundred, CommentMode.Optional, EvaluatorLabelMode.Name, reportShowPhoto: true,
            employeeCanViewResults: true, employeeCanDownloadPdf: true, relationConfigs, OneCompetencyOneQuestion()).Value;

    private static void AddTenParticipants(ReviewCycle cycle, bool sevenWithFiveManagers)
    {
        for (var i = 0; i < 10; i++)
        {
            var managerCount = i < 7 ? 5 : 2;
            var managers = Enumerable.Range(0, managerCount).Select(_ => Guid.NewGuid()).ToList();
            var evaluators = new Dictionary<EvaluatorRelation, IReadOnlyList<Guid>> { [EvaluatorRelation.Manager] = managers };
            cycle.AddParticipants([new ParticipantInput(Guid.NewGuid(), false, evaluators)]).IsSuccess.ShouldBeTrue();
        }
    }

    internal static IReadOnlyList<RelationConfigInput> SelfAndManagerConfigs(int managerMinRequired) =>
    [
        new(EvaluatorRelation.Self, true, null, 1),
        new(EvaluatorRelation.Manager, true, null, managerMinRequired),
        new(EvaluatorRelation.Subordinate, false, null, null),
        new(EvaluatorRelation.Peer, false, null, null),
    ];

    internal static IReadOnlyList<RelationConfigInput> SelfDisabledManagerOnly(int minRequired) =>
    [
        new(EvaluatorRelation.Self, false, null, null),
        new(EvaluatorRelation.Manager, true, null, minRequired),
        new(EvaluatorRelation.Subordinate, false, null, null),
        new(EvaluatorRelation.Peer, false, null, null),
    ];

    internal static IReadOnlyList<RelationConfigInput> SelfOnlyConfig() =>
    [
        new(EvaluatorRelation.Self, true, null, 1),
        new(EvaluatorRelation.Manager, false, null, null),
        new(EvaluatorRelation.Subordinate, false, null, null),
        new(EvaluatorRelation.Peer, false, null, null),
    ];

    internal static IReadOnlyList<CompetencyInput> OneCompetencyOneQuestion() =>
    [
        new("İletişim", null, [new QuestionInput(1, "Ekip içi iletişimi nasıl?", null, null)]),
    ];
}
