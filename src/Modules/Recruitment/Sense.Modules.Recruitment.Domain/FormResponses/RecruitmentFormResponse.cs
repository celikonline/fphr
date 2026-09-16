using Sense.Modules.Recruitment.Domain.FormTemplates;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Recruitment.Domain.FormResponses;

/// <summary>
/// A filled-out instance of a <see cref="RecruitmentFormTemplate"/> against one <see cref="Applications.JobApplication"/>
/// (roadmap flow #3 "Mülakat geri bildirimi", aday kartı "Formlar" sekmesi). Answers are validated/scored against the
/// template's field snapshot at submit time; the template may change afterward without affecting past responses.
/// </summary>
public sealed class RecruitmentFormResponse : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<FieldAnswer> _answers = [];

    private RecruitmentFormResponse()
    {
    }

    private RecruitmentFormResponse(Guid id, Guid tenantId, Guid templateId, Guid jobApplicationId, Guid submittedByEmployeeId, decimal? weightedScore)
        : base(id, tenantId)
    {
        TemplateId = templateId;
        JobApplicationId = jobApplicationId;
        SubmittedByEmployeeId = submittedByEmployeeId;
        SubmittedAt = DateTime.UtcNow;
        WeightedScore = weightedScore;
    }

    public Guid TemplateId { get; private set; }

    public Guid JobApplicationId { get; private set; }

    public Guid SubmittedByEmployeeId { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    /// <summary>Set only for <see cref="RecruitmentFormTemplateType.Scorecard"/> templates: weighted average of ScoreCard field answers (0-100).</summary>
    public decimal? WeightedScore { get; private set; }

    public IReadOnlyList<FieldAnswer> Answers => _answers.AsReadOnly();

    /// <summary>
    /// Validates every required field is answered and every <see cref="FormFieldType.ScoreCard"/> answer parses as
    /// 0-100, then computes <see cref="WeightedScore"/> for a <see cref="RecruitmentFormTemplateType.Scorecard"/> template.
    /// </summary>
    public static Result<RecruitmentFormResponse> Submit(
        Guid tenantId, RecruitmentFormTemplate template, Guid jobApplicationId, Guid submittedByEmployeeId,
        IReadOnlyDictionary<Guid, string?> answersByFieldId, Guid? id = null)
    {
        foreach (var field in template.Fields)
        {
            var hasAnswer = answersByFieldId.TryGetValue(field.Id, out var value) && !string.IsNullOrWhiteSpace(value);
            if (field.IsRequired && !hasAnswer)
            {
                return Error.Validation(RecruitmentDomainErrors.FormResponseFieldRequired, (nameof(field.Label), field.Label));
            }

            if (field.FieldType == FormFieldType.ScoreCard && hasAnswer && !IsValidScore(value))
            {
                return Error.Validation(RecruitmentDomainErrors.FormResponseScoreInvalid, (nameof(field.Label), field.Label));
            }
        }

        decimal? weightedScore = null;
        if (template.TemplateType == RecruitmentFormTemplateType.Scorecard)
        {
            var scoreFields = template.Fields.Where(f => f.FieldType == FormFieldType.ScoreCard).ToList();
            var totalWeight = scoreFields.Sum(f => f.Weight ?? 0);
            var weightedSum = scoreFields.Sum(f =>
                (answersByFieldId.TryGetValue(f.Id, out var value) && decimal.TryParse(value, out var score) ? score : 0) * (f.Weight ?? 0));
            weightedScore = totalWeight == 0 ? 0 : Math.Round(weightedSum / totalWeight, 2);
        }

        var response = new RecruitmentFormResponse(id ?? Guid.CreateVersion7(), tenantId, template.Id, jobApplicationId, submittedByEmployeeId, weightedScore);
        foreach (var field in template.Fields)
        {
            if (answersByFieldId.TryGetValue(field.Id, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                response._answers.Add(new FieldAnswer(field.Id, value));
            }
        }

        return response;
    }

    private static bool IsValidScore(string? value) => decimal.TryParse(value, out var score) && score is >= 0 and <= 100;

    /// <summary>
    /// KVKK imha (G-9-12, Faz 9 gün açılışı TALIMAT'ı madde 4): adayın anonimleştirilmesi sırasında serbest metin
    /// yanıtları (mülakat notları vb.) kaldırılır; <paramref name="scoreCardFieldIds"/> içindeki alanların (sayısal
    /// puan) yanıtları korunur - skor istatistikleri/ortalamaları bozulmaz, yalnızca kişiyi tanımlayan metin kalmaz.
    /// </summary>
    public void ScrubFreeTextAnswers(IReadOnlySet<Guid> scoreCardFieldIds) =>
        _answers.RemoveAll(a => !scoreCardFieldIds.Contains(a.FieldId));
}

/// <summary>One field's answer. Owned collection; only answered (non-blank) fields are stored.</summary>
public sealed class FieldAnswer : AuditableRecord
{
    private FieldAnswer()
    {
    }

    public FieldAnswer(Guid fieldId, string value)
    {
        FieldId = fieldId;
        Value = value;
    }

    public Guid FieldId { get; private set; }

    public string Value { get; private set; } = string.Empty;
}
