using Sense.Modules.Performance.Domain.ReviewCycles;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record RelationConfigDto(EvaluatorRelation Relation, bool IsEnabled, decimal? Weight, int? MinRequired);

public sealed record QuestionDto(Guid Id, int Order, string Text, decimal? Weight, CommentMode? CommentMode);

public sealed record CompetencyDto(Guid Id, string Name, decimal? Weight, IReadOnlyList<QuestionDto> Questions);

public sealed record ReviewResponseDto(Guid QuestionId, decimal Score, string? Comment);

public sealed record ReviewAssignmentDto(
    Guid Id, EvaluatorRelation Relation, Guid EvaluatorEmployeeId, ReviewAssignmentStatus Status, DateTime? CompletedAtUtc, IReadOnlyList<ReviewResponseDto> Responses);

public sealed record ReviewParticipantDto(
    Guid Id, Guid EmployeeId, ReviewParticipantStatus Status, decimal CompetencyProgress, decimal? CompetencyScore, decimal? FinalScore,
    DateTime? CompletedAtUtc, IReadOnlyList<ReviewAssignmentDto> Assignments);

public sealed record ReviewCycleDto(
    Guid Id, string Name, DateOnly PeriodStart, DateOnly PeriodEnd, ReviewKind Kind, RatingScale RatingScale, CommentMode DefaultCommentMode,
    EvaluatorLabelMode EvaluatorLabelMode, bool ReportShowPhoto, bool EmployeeCanViewResults, bool EmployeeCanDownloadPdf,
    ReviewCycleStatus Status, DateTime? PublishedAtUtc, DateTime? CompletedAtUtc, ReviewCompletionMode? CompletedBy,
    IReadOnlyList<RelationConfigDto> RelationConfigs, IReadOnlyList<CompetencyDto> Competencies, IReadOnlyList<ReviewParticipantDto> Participants);

/// <summary>Lightweight row for the admin list page ("grid standardı").</summary>
public sealed record ReviewCycleSummaryDto(
    Guid Id, string Name, DateOnly PeriodStart, DateOnly PeriodEnd, ReviewCycleStatus Status, int ParticipantCount, int CompletedParticipantCount);

/// <summary>One row of "Bana atanan formlar" - a single evaluator assignment with just enough cycle context to render it.</summary>
public sealed record MyAssignmentDto(
    Guid ReviewCycleId, string ReviewCycleName, DateOnly PeriodStart, DateOnly PeriodEnd,
    Guid AssignmentId, EvaluatorRelation Relation, Guid ParticipantEmployeeId, ReviewAssignmentStatus Status);

internal static class ReviewCycleMapper
{
    public static ReviewCycleDto ToDto(ReviewCycle cycle) => new(
        cycle.Id, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd, cycle.Kind, cycle.RatingScale, cycle.DefaultCommentMode,
        cycle.EvaluatorLabelMode, cycle.ReportShowPhoto, cycle.EmployeeCanViewResults, cycle.EmployeeCanDownloadPdf,
        cycle.Status, cycle.PublishedAtUtc, cycle.CompletedAtUtc, cycle.CompletedBy,
        cycle.RelationConfigs.Select(r => new RelationConfigDto(r.Relation, r.MinRequired is not null, r.Weight, r.MinRequired)).ToList(),
        cycle.Competencies.Select(ToDto).ToList(),
        cycle.Participants.Select(ToDto).ToList());

    public static CompetencyDto ToDto(ReviewCompetency competency) => new(
        competency.Id, competency.Name, competency.Weight,
        competency.Questions.OrderBy(q => q.Order).Select(q => new QuestionDto(q.Id, q.Order, q.Text, q.Weight, q.CommentMode)).ToList());

    public static ReviewParticipantDto ToDto(ReviewParticipant participant) => new(
        participant.Id, participant.EmployeeId, participant.Status, participant.CompetencyProgress, participant.CompetencyScore, participant.FinalScore,
        participant.CompletedAtUtc, participant.Assignments.Select(ToDto).ToList());

    public static ReviewAssignmentDto ToDto(ReviewAssignment assignment) => new(
        assignment.Id, assignment.Relation, assignment.EvaluatorEmployeeId, assignment.Status, assignment.CompletedAtUtc,
        assignment.Responses.Select(r => new ReviewResponseDto(r.QuestionId, r.Score, r.Comment)).ToList());

    public static ReviewCycleSummaryDto ToSummaryDto(ReviewCycle cycle) => new(
        cycle.Id, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd, cycle.Status,
        cycle.Participants.Count, cycle.Participants.Count(p => p.Status == ReviewParticipantStatus.Completed));
}
