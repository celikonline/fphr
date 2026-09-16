using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Sense.Modules.Performance.Application.ReviewCycles;

namespace Sense.Modules.Performance.Infrastructure.Reports;

/// <summary>
/// QuestPDF-backed individual report renderer (11.3.10 "Bireysel rapor"). Renders synchronously/live - no background
/// job or stored file, see plan deviation #1 (Documents/Hangfire have no real write path to build on yet).
/// </summary>
public sealed class QuestPdfReviewReportRenderer : IReviewReportRenderer
{
    static QuestPdfReviewReportRenderer() => QuestPDF.Settings.License = LicenseType.Community;

    public byte[] Render(ReviewReportData data) =>
        QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(data.CycleName).FontSize(16).Bold();
                    col.Item().Text(data.Kind == ReviewReportKind.Detailed ? "Bireysel Değerlendirme Raporu - Detaylı" : "Bireysel Değerlendirme Raporu - Özet")
                        .FontSize(11).SemiBold();
                    col.Item().PaddingTop(2).Text($"Dönem: {data.PeriodStart:dd.MM.yyyy} - {data.PeriodEnd:dd.MM.yyyy}    İndirilme tarihi: {data.GeneratedAtUtc:dd.MM.yyyy HH:mm}");
                    col.Item().PaddingBottom(8).LineHorizontal(1);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Element(c => ParticipantCard(c, data));
                    col.Item().Element(c => ScoreSummary(c, data));

                    if (data.Kind == ReviewReportKind.Detailed)
                    {
                        foreach (var competency in data.Competencies)
                        {
                            col.Item().Element(c => CompetencySection(c, competency));
                        }

                        if (data.SkippedEvaluators.Count > 0)
                        {
                            col.Item().Element(c => SkippedSection(c, data.SkippedEvaluators));
                        }
                    }

                    if (data.Goals.Count > 0)
                    {
                        col.Item().Element(c => GoalsSection(c, data.Kind, data.Goals));
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();

    private static void ParticipantCard(QuestPDF.Infrastructure.IContainer container, ReviewReportData data) =>
        container.Background(Colors.Grey.Lighten4).Padding(8).Column(col =>
        {
            col.Item().Text(data.ParticipantName).FontSize(12).Bold();
            if (!string.IsNullOrWhiteSpace(data.ParticipantEmail))
            {
                col.Item().Text($"E-posta: {data.ParticipantEmail}");
            }

            if (!string.IsNullOrWhiteSpace(data.ParticipantPhone))
            {
                col.Item().Text($"Telefon: {data.ParticipantPhone}");
            }

            if (data.ParticipantHireDate is { } hireDate)
            {
                col.Item().Text($"İşe başlama tarihi: {hireDate:dd.MM.yyyy}");
            }

            col.Item().Text($"Puanlama şekli: {data.RatingScaleLabel}");
        });

    private static void ScoreSummary(QuestPDF.Infrastructure.IContainer container, ReviewReportData data) =>
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Yetkinlik puanı").SemiBold();
                col.Item().Text(FormatScore(data.CompetencyScore) + (data.CompetencyWeightUsed is { } cw ? $" (ağırlık %{cw:0.##})" : string.Empty));
            });

            if (data.IsGoalKind)
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("Hedef puanı").SemiBold();
                    col.Item().Text(FormatScore(data.GoalScore) + (data.GoalWeightUsed is { } gw ? $" (ağırlık %{gw:0.##})" : string.Empty));
                });
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text("Nihai puan").SemiBold();
                col.Item().Text(FormatScore(data.FinalScore)).FontSize(12).Bold();
            });
        });

    private static void CompetencySection(QuestPDF.Infrastructure.IContainer container, ReviewReportCompetencyData competency) =>
        container.Column(col =>
        {
            col.Item().PaddingTop(4).Text($"{competency.Name}{(competency.Weight is { } w ? $" (ağırlık %{w:0.##})" : string.Empty)} — {FormatScore(competency.Score)}")
                .FontSize(11).Bold();

            foreach (var question in competency.Questions)
            {
                col.Item().PaddingTop(3).Text(t =>
                {
                    t.Span(question.Text).SemiBold();
                    if (question.Weight is { } qw)
                    {
                        t.Span($"  (ağırlık %{qw:0.##})");
                    }

                    t.Span($"  →  {FormatScore(question.ParticipantScore)}").Bold();
                });
                col.Item().Text(question.CommentModeLabel).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);

                foreach (var answer in question.Answers)
                {
                    col.Item().PaddingLeft(10).Row(row =>
                    {
                        row.ConstantItem(120).Text(answer.EvaluatorLabel);
                        row.ConstantItem(40).Text(answer.Score.ToString("0.##"));
                        row.RelativeItem().Text(answer.Comment ?? string.Empty);
                    });
                }
            }
        });

    private static void SkippedSection(QuestPDF.Infrastructure.IContainer container, IReadOnlyList<ReviewReportSkippedEvaluatorData> skipped) =>
        container.Column(col =>
        {
            col.Item().PaddingTop(6).Text("Tamamlanmayan değerlendiriciler (Atlandı)").FontSize(10).Bold();
            foreach (var s in skipped)
            {
                col.Item().Text($"• {s.EvaluatorLabel} — Atlandı");
            }
        });

    private static void GoalsSection(QuestPDF.Infrastructure.IContainer container, ReviewReportKind kind, IReadOnlyList<ReviewReportGoalData> goals) =>
        container.Column(col =>
        {
            col.Item().PaddingTop(6).Text("Hedefler").FontSize(12).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                    columns.RelativeColumn(1);
                    if (kind == ReviewReportKind.Detailed)
                    {
                        columns.RelativeColumn(3);
                    }
                });

                void Header(string text) => table.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(text).Bold();
                Header("Kategori");
                Header("Hedef");
                Header("Hedef değeri");
                Header("Gerçekleşen");
                Header("Puan");
                if (kind == ReviewReportKind.Detailed)
                {
                    Header("Sonuç yorumu");
                }

                foreach (var goal in goals)
                {
                    table.Cell().Padding(3).Text(goal.CategoryName);
                    table.Cell().Padding(3).Text(goal.Name + (goal.Weight is { } w ? $" (%{w:0.##})" : string.Empty));
                    table.Cell().Padding(3).Text(goal.TargetValue.ToString("0.##"));
                    table.Cell().Padding(3).Text(goal.ActualValue?.ToString("0.##") ?? "-");
                    table.Cell().Padding(3).Text(FormatScore(goal.Points));
                    if (kind == ReviewReportKind.Detailed)
                    {
                        table.Cell().Padding(3).Text(goal.ResultComment ?? string.Empty);
                    }
                }
            });
        });

    private static string FormatScore(decimal? score) => score?.ToString("0.##") ?? "-";
}
