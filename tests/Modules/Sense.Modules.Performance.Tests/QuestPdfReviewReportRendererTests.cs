using System.Text;
using Sense.Modules.Performance.Application.ReviewCycles;
using Sense.Modules.Performance.Infrastructure.Reports;
using Shouldly;
using Xunit;

namespace Sense.Modules.Performance.Tests;

/// <summary>Smoke tests for the QuestPDF renderer - verifies real PDF bytes come out for both report kinds and don't throw on the goal section.</summary>
public sealed class QuestPdfReviewReportRendererTests
{
    private static readonly byte[] PdfMagicBytes = Encoding.ASCII.GetBytes("%PDF");

    [Theory]
    [InlineData(ReviewReportKind.Detailed)]
    [InlineData(ReviewReportKind.Summary)]
    public void Render_CompetencyOnlyData_ProducesValidPdfBytes(ReviewReportKind kind)
    {
        var data = new ReviewReportData(
            kind, "2026 Q1 Değerlendirme", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), DateTime.UtcNow,
            "Ada Lovelace", "ada@example.com", "+90 555 000 0000", new DateOnly(2020, 6, 1),
            "0-100 puan", IsGoalKind: false,
            CompetencyScore: 87.5m, CompetencyWeightUsed: null, GoalScore: null, GoalWeightUsed: null, FinalScore: 87.5m,
            Competencies:
            [
                new ReviewReportCompetencyData(
                    "İletişim", 60m, 90m,
                    [
                        new ReviewReportQuestionData(
                            "Ekip içi iletişimi nasıl?", 50m, "Bu soruya yorum eklemek seçime bağlıdır.", 90m,
                            [new ReviewReportAnswerData("Üstü", 90m, "Çok iyi")]),
                    ]),
            ],
            SkippedEvaluators: [new ReviewReportSkippedEvaluatorData("Diğer")],
            Goals: []);

        var renderer = new QuestPdfReviewReportRenderer();
        var bytes = renderer.Render(data);

        bytes.Length.ShouldBeGreaterThan(0);
        bytes.Take(PdfMagicBytes.Length).ToArray().ShouldBe(PdfMagicBytes);
    }

    [Fact]
    public void Render_WithGoals_ProducesValidPdfBytes()
    {
        var data = new ReviewReportData(
            ReviewReportKind.Detailed, "2026 Q1 Değerlendirme", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), DateTime.UtcNow,
            "Ada Lovelace", null, null, null,
            "0-100 puan", IsGoalKind: true,
            CompetencyScore: 60m, CompetencyWeightUsed: 40m, GoalScore: 100m, GoalWeightUsed: 60m, FinalScore: 84m,
            Competencies: [],
            SkippedEvaluators: [],
            Goals: [new ReviewReportGoalData("Satış", "Q1 satış hedefi", "Açıklama", 100m, 85m, 100m, "Hedefi aştı", null)]);

        var renderer = new QuestPdfReviewReportRenderer();
        var bytes = renderer.Render(data);

        bytes.Length.ShouldBeGreaterThan(0);
        bytes.Take(PdfMagicBytes.Length).ToArray().ShouldBe(PdfMagicBytes);
    }
}
