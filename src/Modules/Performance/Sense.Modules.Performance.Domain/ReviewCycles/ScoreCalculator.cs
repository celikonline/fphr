namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>
/// Pure scoring rules (11.3.10): soru puanı = tamamlanmış atamalardaki değerlendirici puanlarının kategori ağırlıklı
/// ortalaması; yetkinlik puanı = soru ağırlıklı ortalama; genel puan = yetkinlik ağırlıklı ortalama. Her seviyede
/// ağırlık boşsa (veya toplamı 0 ise) 100/n eşit dağıtım (TenantSettings.Performance.EqualWeightWhenBlank, MVP'de sabit true).
/// </summary>
public static class ScoreCalculator
{
    /// <summary>Null when the participant has no completed answers at all (nothing to score yet).</summary>
    public static decimal? CalculateParticipantScore(ReviewCycle cycle, ReviewParticipant participant)
    {
        var competencyScores = new List<(decimal Weight, decimal Score)>();
        foreach (var competency in cycle.Competencies)
        {
            var score = CalculateCompetencyScore(cycle, participant, competency);
            if (score is { } value)
            {
                competencyScores.Add((competency.Weight ?? 0m, value));
            }
        }

        return competencyScores.Count == 0 ? null : WeightedAverage(competencyScores);
    }

    /// <summary>hedef puanı = skalaya göre puan; hedef genel puanı = hedef ağırlıklı ortalama. Only ResultApproved goals count.</summary>
    public static decimal? CalculateGoalScore(ReviewParticipant participant)
    {
        var goalScores = participant.Goals
            .Where(g => g.Status == GoalStatus.ResultApproved && g.Points is not null)
            .Select(g => (Weight: g.Weight ?? 0m, Score: g.Points!.Value))
            .ToList();

        return goalScores.Count == 0 ? null : WeightedAverage(goalScores);
    }

    /// <summary>nihai = yetkinlik ağırlığı × yetkinlik puanı + hedef ağırlığı × hedef puanı (11.3.10); CompetencyWeight boşsa 50/50.</summary>
    public static decimal? CalculateFinalScore(ReviewCycle cycle, decimal? competencyScore, decimal? goalScore, bool includeGoals)
    {
        if (cycle.Kind != ReviewKind.CompetencyAndGoal || !includeGoals || goalScore is null)
        {
            return competencyScore;
        }

        if (competencyScore is null)
        {
            return goalScore;
        }

        var competencyWeight = cycle.CompetencyWeight ?? 50m;
        var goalWeight = 100m - competencyWeight;
        return Math.Round((competencyWeight * competencyScore.Value + goalWeight * goalScore.Value) / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>yetkinlik puanı = soru ağırlıklı ortalama. Public for report rendering.</summary>
    public static decimal? CalculateCompetencyScore(ReviewCycle cycle, ReviewParticipant participant, ReviewCompetency competency)
    {
        var questionScores = new List<(decimal Weight, decimal Score)>();
        foreach (var question in competency.Questions)
        {
            var score = CalculateQuestionScore(cycle, participant, question.Id);
            if (score is { } value)
            {
                questionScores.Add((question.Weight ?? 0m, value));
            }
        }

        return questionScores.Count == 0 ? null : WeightedAverage(questionScores);
    }

    /// <summary>soru puanı = tamamlanmış atamalardaki değerlendirici puanlarının kategori ağırlıklı ortalaması. Public for report rendering.</summary>
    public static decimal? CalculateQuestionScore(ReviewCycle cycle, ReviewParticipant participant, Guid questionId)
    {
        var relationAverages = new List<(decimal Weight, decimal Score)>();
        foreach (var config in cycle.RelationConfigs.Where(c => c.MinRequired is not null))
        {
            var scores = participant.Assignments
                .Where(a => a.Relation == config.Relation && a.Status == ReviewAssignmentStatus.Completed)
                .SelectMany(a => a.Responses.Where(r => r.QuestionId == questionId))
                .Select(r => r.Score)
                .ToList();

            if (scores.Count == 0)
            {
                continue;
            }

            relationAverages.Add((config.Weight ?? 0m, scores.Average()));
        }

        return relationAverages.Count == 0 ? null : WeightedAverage(relationAverages);
    }

    private static decimal WeightedAverage(List<(decimal Weight, decimal Score)> items)
    {
        var totalWeight = items.Sum(i => i.Weight);
        var average = totalWeight <= 0m
            ? items.Average(i => i.Score)
            : items.Sum(i => i.Weight * i.Score) / totalWeight;
        return Math.Round(average, 2, MidpointRounding.AwayFromZero);
    }
}
