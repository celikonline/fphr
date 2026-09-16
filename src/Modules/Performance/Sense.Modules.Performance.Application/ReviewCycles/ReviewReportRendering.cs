namespace Sense.Modules.Performance.Application.ReviewCycles;

public enum ReviewReportKind
{
    Detailed = 0,
    Summary = 1,
}

public sealed record ReviewReportAnswerData(string EvaluatorLabel, decimal Score, string? Comment);

public sealed record ReviewReportSkippedEvaluatorData(string EvaluatorLabel);

public sealed record ReviewReportQuestionData(string Text, decimal? Weight, string CommentModeLabel, decimal? ParticipantScore, IReadOnlyList<ReviewReportAnswerData> Answers);

public sealed record ReviewReportCompetencyData(string Name, decimal? Weight, decimal? Score, IReadOnlyList<ReviewReportQuestionData> Questions);

public sealed record ReviewReportGoalData(
    string CategoryName, string Name, string? Description, decimal TargetValue, decimal? ActualValue, decimal? Points, string? ResultComment, decimal? Weight);

/// <summary>Fully resolved report content (names/labels already applied) - the renderer only lays it out, no business logic.</summary>
public sealed record ReviewReportData(
    ReviewReportKind Kind, string CycleName, DateOnly PeriodStart, DateOnly PeriodEnd, DateTime GeneratedAtUtc,
    string ParticipantName, string? ParticipantEmail, string? ParticipantPhone, DateOnly? ParticipantHireDate,
    string RatingScaleLabel, bool IsGoalKind,
    decimal? CompetencyScore, decimal? CompetencyWeightUsed, decimal? GoalScore, decimal? GoalWeightUsed, decimal? FinalScore,
    IReadOnlyList<ReviewReportCompetencyData> Competencies, IReadOnlyList<ReviewReportSkippedEvaluatorData> SkippedEvaluators,
    IReadOnlyList<ReviewReportGoalData> Goals);

/// <summary>QuestPDF-backed renderer (Infrastructure) - kept behind a port so Application stays free of the rendering library.</summary>
public interface IReviewReportRenderer
{
    byte[] Render(ReviewReportData data);
}
