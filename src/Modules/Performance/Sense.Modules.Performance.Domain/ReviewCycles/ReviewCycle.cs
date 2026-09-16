using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>Input shape for one relation category's config (Yetkinlik adımı: aktif/pasif + ağırlık; Erişim adımı: min değerlendirici).</summary>
public sealed record RelationConfigInput(EvaluatorRelation Relation, bool IsEnabled, decimal? Weight, int? MinRequired);

/// <summary>Input shape for one question under a competency (Sorular adımı).</summary>
public sealed record QuestionInput(int Order, string Text, decimal? Weight, CommentMode? CommentMode);

/// <summary>Input shape for one competency (Yetkinlik adımı).</summary>
public sealed record CompetencyInput(string Name, decimal? Weight, IReadOnlyList<QuestionInput> Questions);

/// <summary>
/// A performance review period (Kolay İK "Değerlendirme"). The wizard's "Kaydet ve bitir" creates the whole aggregate
/// (config + competencies/questions + relation categories) in one call - see plan deviation #3, no separate
/// ReviewTemplate library/screen for this MVP, everything is ad hoc per cycle.
/// </summary>
public sealed class ReviewCycle : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<ReviewCompetency> _competencies = [];
    private readonly List<ReviewRelationConfig> _relationConfigs = [];
    private readonly List<ReviewParticipant> _participants = [];

    private ReviewCycle()
    {
    }

    private ReviewCycle(
        Guid id, Guid tenantId, string name, DateOnly periodStart, DateOnly periodEnd, ReviewKind kind,
        RatingScale ratingScale, CommentMode defaultCommentMode, EvaluatorLabelMode evaluatorLabelMode, bool reportShowPhoto,
        bool employeeCanViewResults, bool employeeCanDownloadPdf, GoalEntryBy goalEntryBy, decimal? competencyWeight)
        : base(id, tenantId)
    {
        Name = name;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Kind = kind;
        RatingScale = ratingScale;
        DefaultCommentMode = defaultCommentMode;
        EvaluatorLabelMode = evaluatorLabelMode;
        ReportShowPhoto = reportShowPhoto;
        EmployeeCanViewResults = employeeCanViewResults;
        EmployeeCanDownloadPdf = employeeCanDownloadPdf;
        GoalEntryBy = goalEntryBy;
        CompetencyWeight = competencyWeight;
        Status = ReviewCycleStatus.Draft;
    }

    public string Name { get; private set; } = string.Empty;

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public ReviewKind Kind { get; private set; }

    public RatingScale RatingScale { get; private set; }

    public CommentMode DefaultCommentMode { get; private set; }

    /// <summary>"Değerlendiricilerin raporda görünümü": ad mı, yalnız kategori adı mı.</summary>
    public EvaluatorLabelMode EvaluatorLabelMode { get; private set; }

    public bool ReportShowPhoto { get; private set; }

    /// <summary>"Değerlendirilenler sonuçlara erişim: sonuçları görebilsin".</summary>
    public bool EmployeeCanViewResults { get; private set; }

    /// <summary>"Değerlendirilenler sonuçlara erişim: PDF raporunu indirebilsin".</summary>
    public bool EmployeeCanDownloadPdf { get; private set; }

    /// <summary>Yalnız Kind=CompetencyAndGoal'da anlamlı: hedef girişini/sonuç girişini çalışan mı yapar, birinci yönetici mi.</summary>
    public GoalEntryBy GoalEntryBy { get; private set; }

    /// <summary>nihai puan karışımında yetkinlik tarafının ağırlığı (0-100); null → 50/50. Yalnız Kind=CompetencyAndGoal'da kullanılır.</summary>
    public decimal? CompetencyWeight { get; private set; }

    public ReviewCycleStatus Status { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public ReviewCompletionMode? CompletedBy { get; private set; }

    public IReadOnlyList<ReviewCompetency> Competencies => _competencies.AsReadOnly();

    public IReadOnlyList<ReviewRelationConfig> RelationConfigs => _relationConfigs.AsReadOnly();

    public IReadOnlyList<ReviewParticipant> Participants => _participants.AsReadOnly();

    public static Result<ReviewCycle> Create(
        Guid tenantId, string name, DateOnly periodStart, DateOnly periodEnd, ReviewKind kind,
        RatingScale ratingScale, CommentMode defaultCommentMode, EvaluatorLabelMode evaluatorLabelMode, bool reportShowPhoto,
        bool employeeCanViewResults, bool employeeCanDownloadPdf, GoalEntryBy goalEntryBy, decimal? competencyWeight,
        IReadOnlyList<RelationConfigInput> relationConfigs, IReadOnlyList<CompetencyInput> competencies, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(PerformanceDomainErrors.CycleNameRequired);
        }

        if (periodEnd < periodStart)
        {
            return Error.Validation(PerformanceDomainErrors.CyclePeriodInvalid);
        }

        var relationValidation = ValidateRelationConfigs(relationConfigs);
        if (relationValidation is not null)
        {
            return relationValidation;
        }

        var competencyValidation = ValidateCompetencies(competencies);
        if (competencyValidation is not null)
        {
            return competencyValidation;
        }

        var cycle = new ReviewCycle(
            id ?? Guid.CreateVersion7(), tenantId, Guard.MaxLength(name.Trim(), PerformanceLimits.NameMaxLength), periodStart, periodEnd, kind,
            ratingScale, defaultCommentMode, evaluatorLabelMode, reportShowPhoto, employeeCanViewResults, employeeCanDownloadPdf, goalEntryBy, competencyWeight);

        cycle.ReplaceRelationConfigsUnchecked(relationConfigs);
        cycle.ReplaceCompetenciesUnchecked(competencies);
        return cycle;
    }

    /// <summary>Replaces config while still Draft (sihirbaz adımlarına geri dönüp düzenleme). Kind is fixed at Create time.</summary>
    public Result UpdateDraft(
        string name, DateOnly periodStart, DateOnly periodEnd, RatingScale ratingScale, CommentMode defaultCommentMode,
        EvaluatorLabelMode evaluatorLabelMode, bool reportShowPhoto, bool employeeCanViewResults, bool employeeCanDownloadPdf,
        GoalEntryBy goalEntryBy, decimal? competencyWeight,
        IReadOnlyList<RelationConfigInput> relationConfigs, IReadOnlyList<CompetencyInput> competencies)
    {
        if (Status != ReviewCycleStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotDraft);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(PerformanceDomainErrors.CycleNameRequired);
        }

        if (periodEnd < periodStart)
        {
            return Error.Validation(PerformanceDomainErrors.CyclePeriodInvalid);
        }

        var relationValidation = ValidateRelationConfigs(relationConfigs);
        if (relationValidation is not null)
        {
            return relationValidation;
        }

        var competencyValidation = ValidateCompetencies(competencies);
        if (competencyValidation is not null)
        {
            return competencyValidation;
        }

        Name = Guard.MaxLength(name.Trim(), PerformanceLimits.NameMaxLength);
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        RatingScale = ratingScale;
        DefaultCommentMode = defaultCommentMode;
        EvaluatorLabelMode = evaluatorLabelMode;
        ReportShowPhoto = reportShowPhoto;
        EmployeeCanViewResults = employeeCanViewResults;
        EmployeeCanDownloadPdf = employeeCanDownloadPdf;
        GoalEntryBy = goalEntryBy;
        CompetencyWeight = competencyWeight;
        ReplaceRelationConfigsUnchecked(relationConfigs);
        ReplaceCompetenciesUnchecked(competencies);
        return Result.Success();
    }

    /// <summary>
    /// Adds participants with their per-category evaluators (Katılımcılar adımı). Assignments are created immediately
    /// (Pending) - they only become fillable once the cycle is Published (see plan simplification: no separate
    /// pre-publish "selection" shape). "Kendisi" kategorisine katılımcı dışında kişi eklenemez: the self evaluator is
    /// always the participant, never supplied by the caller.
    /// </summary>
    public Result AddParticipants(IReadOnlyList<ParticipantInput> participants)
    {
        if (Status != ReviewCycleStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotDraft);
        }

        if (participants.Count == 0)
        {
            return Error.Validation(PerformanceDomainErrors.ParticipantRequired);
        }

        if (_participants.Count + participants.Count > PerformanceLimits.MaxParticipants)
        {
            return Error.Validation(PerformanceDomainErrors.ParticipantLimitExceeded, (nameof(PerformanceLimits.MaxParticipants), PerformanceLimits.MaxParticipants));
        }

        foreach (var input in participants)
        {
            if (_participants.Any(p => p.EmployeeId == input.EmployeeId))
            {
                return Error.Validation(PerformanceDomainErrors.ParticipantAlreadyAdded, (nameof(input.EmployeeId), input.EmployeeId));
            }

            foreach (var (relation, evaluatorIds) in input.Evaluators)
            {
                if (relation == EvaluatorRelation.Self)
                {
                    return Error.Validation(PerformanceDomainErrors.SelfEvaluatorMustBeParticipant);
                }

                var config = _relationConfigs.FirstOrDefault(r => r.Relation == relation);
                if ((config is null || config.MinRequired is null) && evaluatorIds.Count > 0)
                {
                    return Error.Rule(PerformanceDomainErrors.RelationDisabled, (nameof(relation), relation));
                }
            }
        }

        foreach (var input in participants)
        {
            var participant = ReviewParticipant.Create(TenantId, Id, input.EmployeeId, input.IncludeGoals);
            var selfConfig = _relationConfigs.FirstOrDefault(r => r.Relation == EvaluatorRelation.Self);
            if (selfConfig?.MinRequired is not null)
            {
                participant.AddAssignment(EvaluatorRelation.Self, input.EmployeeId);
            }

            foreach (var (relation, evaluatorIds) in input.Evaluators)
            {
                foreach (var evaluatorId in evaluatorIds)
                {
                    participant.AddAssignment(relation, evaluatorId);
                }
            }

            _participants.Add(participant);
        }

        return Result.Success();
    }

    /// <summary>
    /// Draft→Published: validates the "üst sınır = katılımcılar arasında o kategoride en az atanan sayı" rule against
    /// the participants already added, then opens the cycle for form-filling.
    /// </summary>
    public Result Publish(DateTime now)
    {
        if (Status != ReviewCycleStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotDraft);
        }

        if (_participants.Count == 0)
        {
            return Error.Validation(PerformanceDomainErrors.ParticipantRequired);
        }

        foreach (var config in _relationConfigs.Where(c => c.MinRequired is not null and > 0 && c.Relation != EvaluatorRelation.Self))
        {
            var minAssignedAcrossParticipants = _participants.Min(p => p.Assignments.Count(a => a.Relation == config.Relation));
            if (config.MinRequired > minAssignedAcrossParticipants)
            {
                return Error.Validation(
                    PerformanceDomainErrors.MinEvaluatorsExceedsMax,
                    (nameof(config.Relation), config.Relation), ("max", minAssignedAcrossParticipants));
            }
        }

        Status = ReviewCycleStatus.Published;
        PublishedAtUtc = now;
        Raise(new ReviewCyclePublished(Id, TenantId));
        return Result.Success();
    }

    /// <summary>Records one evaluator's full form submission (all questions of the cycle's competencies at once).</summary>
    public Result SubmitResponse(
        Guid assignmentId, Guid submittingEmployeeId, IReadOnlyList<(Guid QuestionId, decimal Score, string? Comment)> answers, DateTime now)
    {
        if (Status != ReviewCycleStatus.Published)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotPublished);
        }

        var participant = _participants.FirstOrDefault(p => p.Assignments.Any(a => a.Id == assignmentId));
        var assignment = participant?.Assignments.FirstOrDefault(a => a.Id == assignmentId);
        if (participant is null || assignment is null)
        {
            return Error.NotFound(PerformanceDomainErrors.AssignmentNotFound);
        }

        if (assignment.EvaluatorEmployeeId != submittingEmployeeId)
        {
            return Error.Forbidden(PerformanceDomainErrors.AssignmentAssigneeMismatch);
        }

        if (assignment.Status is not (ReviewAssignmentStatus.Pending or ReviewAssignmentStatus.InProgress))
        {
            return Error.Rule(PerformanceDomainErrors.AssignmentNotPending);
        }

        var allQuestions = _competencies.SelectMany(c => c.Questions).ToList();
        var answeredIds = answers.Select(a => a.QuestionId).ToHashSet();
        if (allQuestions.Any(q => !answeredIds.Contains(q.Id)) || answers.Count != allQuestions.Count)
        {
            return Error.Validation(PerformanceDomainErrors.ResponseAnswersIncomplete);
        }

        foreach (var answer in answers)
        {
            var question = allQuestions.FirstOrDefault(q => q.Id == answer.QuestionId);
            if (question is null)
            {
                return Error.Validation(PerformanceDomainErrors.ResponseUnknownQuestion, (nameof(answer.QuestionId), answer.QuestionId));
            }

            var mode = question.CommentMode ?? DefaultCommentMode;
            if (mode == CommentMode.Required && string.IsNullOrWhiteSpace(answer.Comment))
            {
                return Error.Validation(PerformanceDomainErrors.ResponseCommentRequired, (nameof(question.Text), question.Text));
            }
        }

        assignment.Complete(answers, now);
        participant.RecalculateProgress();
        Raise(new ReviewAssignmentCompleted(Id, TenantId, participant.EmployeeId, assignment.Id));

        if (participant.AllAssignmentsCompleted && participant.AllGoalsResultApproved)
        {
            CompleteParticipant(participant, now, ReviewCompletionMode.Natural);
        }

        return Result.Success();
    }

    /// <summary>Hedef ekle (yalnız Kind=CompetencyAndGoal ve katılımcının IncludeGoals'u açıkken).</summary>
    public Result<Guid> AddGoal(
        Guid participantId, Guid categoryId, string name, string? description, decimal targetValue,
        IReadOnlyList<GoalScaleRange> scale, decimal? weight, Guid enteredBy)
    {
        if (Kind != ReviewKind.CompetencyAndGoal)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotEnabledForParticipant);
        }

        var participant = _participants.FirstOrDefault(p => p.Id == participantId);
        if (participant is null)
        {
            return Error.NotFound(PerformanceDomainErrors.ParticipantNotFound);
        }

        if (!participant.IncludeGoals)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotEnabledForParticipant);
        }

        if (categoryId == Guid.Empty)
        {
            return Error.Validation(PerformanceDomainErrors.GoalCategoryRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(PerformanceDomainErrors.GoalNameRequired);
        }

        if (scale.Count == 0)
        {
            return Error.Validation(PerformanceDomainErrors.GoalScaleRequired);
        }

        var goal = participant.AddGoal(categoryId, Guard.MaxLength(name.Trim(), PerformanceLimits.NameMaxLength), description, targetValue, scale, weight, enteredBy);
        return goal.Id;
    }

    /// <summary>"Hedefleri onayla": katılımcının tüm Taslak hedeflerini toplu olarak onaylar.</summary>
    public Result ApproveGoals(Guid participantId, Guid approvedBy)
    {
        var participant = _participants.FirstOrDefault(p => p.Id == participantId);
        if (participant is null)
        {
            return Error.NotFound(PerformanceDomainErrors.ParticipantNotFound);
        }

        var draftGoals = participant.Goals.Where(g => g.Status == GoalStatus.Draft).ToList();
        if (draftGoals.Count == 0)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNoDraftGoals);
        }

        foreach (var goal in draftGoals)
        {
            goal.Approve(approvedBy);
        }

        Raise(new GoalsApproved(Id, TenantId, participant.EmployeeId, draftGoals.Count));
        return Result.Success();
    }

    /// <summary>"Sonuç gir": tek bir hedefin gerçekleşen değerini kaydeder, skalaya göre puanı hesaplar.</summary>
    public Result EnterGoalResult(Guid goalId, decimal actualValue, string? resultComment, Guid enteredBy)
    {
        var goal = _participants.SelectMany(p => p.Goals).FirstOrDefault(g => g.Id == goalId);
        if (goal is null)
        {
            return Error.NotFound(PerformanceDomainErrors.GoalNotFound);
        }

        return goal.EnterResult(actualValue, resultComment, enteredBy);
    }

    /// <summary>"Sonuçları onayla": katılımcının tüm "Sonuç girildi" hedeflerini toplu onaylar; hedef akışı tamamlanan katılımcının doğal tamamlanmasını tetikleyebilir.</summary>
    public Result ApproveGoalResults(Guid participantId, Guid approvedBy, DateTime now)
    {
        var participant = _participants.FirstOrDefault(p => p.Id == participantId);
        if (participant is null)
        {
            return Error.NotFound(PerformanceDomainErrors.ParticipantNotFound);
        }

        var enteredGoals = participant.Goals.Where(g => g.Status == GoalStatus.ResultEntered).ToList();
        if (enteredGoals.Count == 0)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNoResultEnteredGoals);
        }

        foreach (var goal in enteredGoals)
        {
            goal.ApproveResult(approvedBy);
        }

        Raise(new GoalResultsApproved(Id, TenantId, participant.EmployeeId, enteredGoals.Count));

        if (Status == ReviewCycleStatus.Published && participant.Status != ReviewParticipantStatus.Completed
            && participant.AllAssignmentsCompleted && participant.AllGoalsResultApproved)
        {
            CompleteParticipant(participant, now, ReviewCompletionMode.Natural);
        }

        return Result.Success();
    }

    /// <summary>
    /// "Değerlendirmeyi tamamla": processes every not-yet-completed participant who meets the per-category minimum,
    /// marks their remaining assignments Skipped, computes their score. Participants who don't yet qualify are left
    /// untouched - the command can be re-run later (design doc's own open-question resolution: "dönem açık kalır,
    /// komut tekrar çalıştırılabilir"). The cycle itself becomes Completed once every participant has completed.
    /// </summary>
    public Result<int> ForceComplete(DateTime now)
    {
        if (Status != ReviewCycleStatus.Published)
        {
            return Error.Rule(PerformanceDomainErrors.CycleNotPublished);
        }

        var completedCount = 0;
        foreach (var participant in _participants.Where(p => p.Status != ReviewParticipantStatus.Completed))
        {
            if (!IsEligibleForForceComplete(participant))
            {
                continue;
            }

            foreach (var assignment in participant.Assignments.Where(a => a.Status is ReviewAssignmentStatus.Pending or ReviewAssignmentStatus.InProgress))
            {
                assignment.Skip();
            }

            participant.RecalculateProgress();
            CompleteParticipant(participant, now, ReviewCompletionMode.Forced);
            completedCount++;
        }

        if (completedCount == 0)
        {
            return Error.Rule(PerformanceDomainErrors.ForceCompleteNoEligible);
        }

        Raise(new ReviewCycleForceCompleted(Id, TenantId, completedCount));
        return completedCount;
    }

    private bool IsEligibleForForceComplete(ReviewParticipant participant)
    {
        foreach (var config in _relationConfigs.Where(c => c.MinRequired is not null))
        {
            var completed = participant.Assignments.Count(a => a.Relation == config.Relation && a.Status == ReviewAssignmentStatus.Completed);
            var required = config.Relation == EvaluatorRelation.Self ? PerformanceLimits.SelfRequiredValue : config.MinRequired!.Value;
            if (completed < required)
            {
                return false;
            }
        }

        // Yetkinlik + Hedef: hedef durumu "Sonuçlar onaylandı" değilse tamamlanamaz (11.3.10).
        return participant.AllGoalsResultApproved;
    }

    private void CompleteParticipant(ReviewParticipant participant, DateTime now, ReviewCompletionMode mode)
    {
        var competencyScore = ScoreCalculator.CalculateParticipantScore(this, participant);
        var goalScore = ScoreCalculator.CalculateGoalScore(participant);
        var finalScore = ScoreCalculator.CalculateFinalScore(this, competencyScore, goalScore, participant.IncludeGoals);
        participant.Complete(competencyScore, goalScore, finalScore, now);
        Raise(new ReviewParticipantCompleted(Id, TenantId, participant.EmployeeId, mode));

        if (_participants.All(p => p.Status == ReviewParticipantStatus.Completed))
        {
            Status = ReviewCycleStatus.Completed;
            CompletedAtUtc = now;
            CompletedBy = mode;
        }
    }

    private static Error? ValidateRelationConfigs(IReadOnlyList<RelationConfigInput> configs)
    {
        var relations = Enum.GetValues<EvaluatorRelation>();
        if (configs.Count != relations.Length || relations.Any(r => configs.Count(c => c.Relation == r) != 1))
        {
            return Error.Validation(PerformanceDomainErrors.RelationConfigInvalid);
        }

        var selfConfig = configs.Single(c => c.Relation == EvaluatorRelation.Self);
        if (selfConfig.IsEnabled && selfConfig.MinRequired is not (null or PerformanceLimits.SelfRequiredValue))
        {
            return Error.Validation(PerformanceDomainErrors.RelationConfigInvalid);
        }

        if (!configs.Any(c => c.IsEnabled))
        {
            return Error.Validation(PerformanceDomainErrors.RelationConfigInvalid);
        }

        foreach (var config in configs.Where(c => c.IsEnabled && c.Relation != EvaluatorRelation.Self))
        {
            if (config.MinRequired is null or < 1)
            {
                return Error.Validation(PerformanceDomainErrors.RelationConfigInvalid);
            }
        }

        return null;
    }

    private static Error? ValidateCompetencies(IReadOnlyList<CompetencyInput> competencies)
    {
        if (competencies.Count == 0 || competencies.Count > PerformanceLimits.MaxCompetencies)
        {
            return Error.Validation(PerformanceDomainErrors.CompetencyRequired);
        }

        foreach (var competency in competencies)
        {
            if (string.IsNullOrWhiteSpace(competency.Name))
            {
                return Error.Validation(PerformanceDomainErrors.CompetencyNameRequired);
            }

            if (competency.Questions.Count == 0 || competency.Questions.Count > PerformanceLimits.MaxQuestionsPerCompetency)
            {
                return Error.Validation(PerformanceDomainErrors.QuestionRequired);
            }

            if (competency.Questions.Any(q => string.IsNullOrWhiteSpace(q.Text)))
            {
                return Error.Validation(PerformanceDomainErrors.QuestionTextRequired);
            }
        }

        return null;
    }

    private void ReplaceRelationConfigsUnchecked(IReadOnlyList<RelationConfigInput> configs)
    {
        _relationConfigs.Clear();
        foreach (var input in configs)
        {
            _relationConfigs.Add(new ReviewRelationConfig(Id, TenantId, input.Relation, input.IsEnabled ? input.Weight : null, input.IsEnabled ? input.MinRequired : null));
        }
    }

    private void ReplaceCompetenciesUnchecked(IReadOnlyList<CompetencyInput> competencies)
    {
        _competencies.Clear();
        foreach (var input in competencies)
        {
            var competency = new ReviewCompetency(TenantId, Id, Guard.MaxLength(input.Name.Trim(), PerformanceLimits.NameMaxLength), input.Weight);
            foreach (var question in input.Questions)
            {
                competency.AddQuestion(TenantId, question.Order, Guard.MaxLength(question.Text.Trim(), PerformanceLimits.TextMaxLength), question.Weight, question.CommentMode);
            }

            _competencies.Add(competency);
        }
    }
}

/// <summary>Per-participant evaluator selection for <see cref="ReviewCycle.AddParticipants"/> (Manager/Subordinate/Peer only - Self is implicit).</summary>
public sealed record ParticipantInput(Guid EmployeeId, bool IncludeGoals, IReadOnlyDictionary<EvaluatorRelation, IReadOnlyList<Guid>> Evaluators);

public sealed record ReviewCyclePublished(Guid ReviewCycleId, Guid TenantId) : DomainEvent;

public sealed record ReviewAssignmentCompleted(Guid ReviewCycleId, Guid TenantId, Guid ParticipantEmployeeId, Guid AssignmentId) : DomainEvent;

public sealed record ReviewParticipantCompleted(Guid ReviewCycleId, Guid TenantId, Guid ParticipantEmployeeId, ReviewCompletionMode CompletionMode) : DomainEvent;

public sealed record ReviewCycleForceCompleted(Guid ReviewCycleId, Guid TenantId, int ParticipantsCompleted) : DomainEvent;

public sealed record GoalsApproved(Guid ReviewCycleId, Guid TenantId, Guid ParticipantEmployeeId, int GoalCount) : DomainEvent;

public sealed record GoalResultsApproved(Guid ReviewCycleId, Guid TenantId, Guid ParticipantEmployeeId, int GoalCount) : DomainEvent;
