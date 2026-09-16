using Sense.Modules.Employee.Contracts;
using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record ReviewReportFileDto(byte[] Content, string FileName);

/// <summary>
/// Bireysel rapor indirme. Yalnız Completed katılımcılar için üretilir (natural veya ForceComplete ile). Kendi raporunu
/// indiren katılımcı ayrıca ReviewCycle.EmployeeCanDownloadPdf'i açık bulmalı; HR/Manager için bu kısıt yok.
/// Rapor canlı/senkron QuestPDF ile üretilir - ayrı bir dosya/ReviewReport tablosu yok (bkz. plan sapması #1).
/// </summary>
[RequiresPermission("performance.report.download")]
public sealed record GenerateReviewReportQuery(
    Guid ReviewCycleId, Guid ParticipantId, Guid ParticipantEmployeeId, ReviewReportKind Kind, Guid ActorEmployeeId)
    : IQuery<ReviewReportFileDto>, IScopedRequest
{
    public Guid? TargetEmployeeId => ParticipantEmployeeId;
}

public sealed class GenerateReviewReportHandler(
    ITenantContext tenant, IReviewCycleRepository cycles, IEmployeeContactLookup contacts, IEmployeeProfileLookup profiles,
    IGoalCategoryRepository goalCategories, IReviewReportRenderer renderer, TimeProvider clock)
    : IQueryHandler<GenerateReviewReportQuery, ReviewReportFileDto>
{
    public async Task<Result<ReviewReportFileDto>> Handle(GenerateReviewReportQuery query, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(query.ReviewCycleId, cancellationToken);
        if (existing.IsFailure)
        {
            return existing.Error;
        }

        var cycle = existing.Value;
        var participant = cycle.Participants.FirstOrDefault(p => p.Id == query.ParticipantId);
        if (participant is null)
        {
            return Error.NotFound(PerformanceDomainErrors.ParticipantNotFound);
        }

        if (participant.Status != ReviewParticipantStatus.Completed)
        {
            return Error.Rule(PerformanceErrors.ReportNotReady);
        }

        var isSelf = query.ActorEmployeeId == participant.EmployeeId;
        if (isSelf && !cycle.EmployeeCanDownloadPdf)
        {
            return Error.Forbidden(PerformanceErrors.ReportSelfDownloadDisabled);
        }

        var evaluatorIds = participant.Assignments.Select(a => a.EvaluatorEmployeeId).Distinct().ToList();
        var allIds = evaluatorIds.Append(participant.EmployeeId).Distinct().ToList();
        var contactList = await contacts.GetContactsAsync(tenant.TenantId, allIds, cancellationToken);
        var contactsById = contactList.ToDictionary(c => c.EmployeeId);
        var profileList = await profiles.GetProfileDetailsAsync(tenant.TenantId, [participant.EmployeeId], cancellationToken);
        var profile = profileList.FirstOrDefault();

        string LabelFor(Guid employeeId, EvaluatorRelation relation) =>
            cycle.EvaluatorLabelMode == EvaluatorLabelMode.Name && contactsById.TryGetValue(employeeId, out var c)
                ? $"{c.FirstName} {c.LastName}".Trim()
                : EvaluatorRelationLabels.Turkish(relation);

        var competencyDatas = cycle.Competencies.Select(competency =>
        {
            var questionDatas = competency.Questions.OrderBy(q => q.Order).Select(question =>
            {
                var score = ScoreCalculator.CalculateQuestionScore(cycle, participant, question.Id);
                var answers = participant.Assignments
                    .Where(a => a.Status == ReviewAssignmentStatus.Completed)
                    .Select(a => (Assignment: a, Response: a.Responses.FirstOrDefault(r => r.QuestionId == question.Id)))
                    .Where(x => x.Response is not null)
                    .Select(x => new ReviewReportAnswerData(LabelFor(x.Assignment.EvaluatorEmployeeId, x.Assignment.Relation), x.Response!.Score, x.Response.Comment))
                    .ToList();
                var commentMode = CommentModeLabels.Turkish(question.CommentMode ?? cycle.DefaultCommentMode);
                return new ReviewReportQuestionData(question.Text, question.Weight, commentMode, score, answers);
            }).ToList();

            var competencyScore = ScoreCalculator.CalculateCompetencyScore(cycle, participant, competency);
            return new ReviewReportCompetencyData(competency.Name, competency.Weight, competencyScore, questionDatas);
        }).ToList();

        var skipped = participant.Assignments
            .Where(a => a.Status == ReviewAssignmentStatus.Skipped)
            .Select(a => new ReviewReportSkippedEvaluatorData(LabelFor(a.EvaluatorEmployeeId, a.Relation)))
            .ToList();

        Dictionary<Guid, string> categoryNamesById = participant.Goals.Count == 0
            ? []
            : (await goalCategories.ListAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var goalDatas = participant.Goals
            .Select(g => new ReviewReportGoalData(
                categoryNamesById.GetValueOrDefault(g.CategoryId, g.CategoryId.ToString()), g.Name, g.Description, g.TargetValue, g.ActualValue, g.Points, g.ResultComment, g.Weight))
            .ToList();

        var isGoalKind = cycle.Kind == ReviewKind.CompetencyAndGoal && participant.IncludeGoals;
        var competencyWeightUsed = isGoalKind ? cycle.CompetencyWeight ?? 50m : (decimal?)null;
        var goalWeightUsed = isGoalKind ? 100m - (cycle.CompetencyWeight ?? 50m) : (decimal?)null;

        var participantName = contactsById.TryGetValue(participant.EmployeeId, out var pc) ? $"{pc.FirstName} {pc.LastName}".Trim() : string.Empty;
        var data = new ReviewReportData(
            query.Kind, cycle.Name, cycle.PeriodStart, cycle.PeriodEnd, clock.GetUtcNow().UtcDateTime,
            participantName, contactsById.GetValueOrDefault(participant.EmployeeId)?.WorkEmail, profile?.WorkPhone, profile?.HireDate,
            RatingScaleLabels.Turkish(cycle.RatingScale), isGoalKind,
            participant.CompetencyScore, competencyWeightUsed, participant.GoalScore, goalWeightUsed, participant.FinalScore,
            competencyDatas, skipped, goalDatas);

        var pdf = renderer.Render(data);
        var fileName = $"{Sanitize(cycle.Name)}-{Sanitize(participantName)}.pdf";
        return new ReviewReportFileDto(pdf, fileName);
    }

    private static string Sanitize(string value) => string.Join('-', value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
}

internal static class EvaluatorRelationLabels
{
    public static string Turkish(EvaluatorRelation relation) => relation switch
    {
        EvaluatorRelation.Self => "Kendisi",
        EvaluatorRelation.Manager => "Üstü",
        EvaluatorRelation.Subordinate => "Astı",
        EvaluatorRelation.Peer => "Diğer",
        _ => relation.ToString(),
    };
}

internal static class CommentModeLabels
{
    public static string Turkish(CommentMode mode) => mode switch
    {
        CommentMode.Required => "Bu soruya yorum eklemek zorunludur.",
        CommentMode.Disabled => "Bu soruya yorum eklemek kapatılmıştır.",
        _ => "Bu soruya yorum eklemek seçime bağlıdır.",
    };
}

internal static class RatingScaleLabels
{
    public static string Turkish(RatingScale scale) => scale switch
    {
        RatingScale.FivePointAgreement => "5'li katılım ölçeği",
        _ => "0–100 puan",
    };
}
