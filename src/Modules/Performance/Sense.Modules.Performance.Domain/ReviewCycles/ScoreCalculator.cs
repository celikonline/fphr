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
            var questionScores = new List<(decimal Weight, decimal Score)>();
            foreach (var question in competency.Questions)
            {
                var relationScore = CalculateQuestionScore(cycle, participant, question.Id);
                if (relationScore is { } score)
                {
                    questionScores.Add((question.Weight ?? 0m, score));
                }
            }

            if (questionScores.Count == 0)
            {
                continue;
            }

            competencyScores.Add((competency.Weight ?? 0m, WeightedAverage(questionScores)));
        }

        return competencyScores.Count == 0 ? null : WeightedAverage(competencyScores);
    }

    private static decimal? CalculateQuestionScore(ReviewCycle cycle, ReviewParticipant participant, Guid questionId)
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
