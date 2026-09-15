using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>An employee being evaluated in a cycle, owning their evaluator assignments.</summary>
public sealed class ReviewParticipant : TenantEntity<Guid>
{
    private readonly List<ReviewAssignment> _assignments = [];

    private ReviewParticipant()
    {
    }

    private ReviewParticipant(Guid tenantId, Guid reviewCycleId, Guid employeeId, bool includeGoals)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewCycleId = reviewCycleId;
        EmployeeId = employeeId;
        IncludeGoals = includeGoals;
        Status = ReviewParticipantStatus.Pending;
    }

    public Guid ReviewCycleId { get; private set; }

    public Guid EmployeeId { get; private set; }

    /// <summary>Katılımcılar adımı "Hedef" switch'i. Hedef akışı bu MVP'de yok (bkz. ReviewKind.CompetencyAndGoal notu) - alan ileriye dönük saklanır.</summary>
    public bool IncludeGoals { get; private set; }

    public ReviewParticipantStatus Status { get; private set; }

    /// <summary>tamamlanan atama / toplam atama (0-100).</summary>
    public decimal CompetencyProgress { get; private set; }

    public decimal? CompetencyScore { get; private set; }

    /// <summary>CompetencyScore ile eşit (Kind=CompetencyAndGoal ve hedef ağırlıklandırması bu MVP'de yok).</summary>
    public decimal? FinalScore { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyList<ReviewAssignment> Assignments => _assignments.AsReadOnly();

    public bool AllAssignmentsCompleted => _assignments.Count > 0 && _assignments.All(a => a.Status == ReviewAssignmentStatus.Completed);

    internal static ReviewParticipant Create(Guid tenantId, Guid reviewCycleId, Guid employeeId, bool includeGoals) =>
        new(tenantId, reviewCycleId, employeeId, includeGoals);

    internal void AddAssignment(EvaluatorRelation relation, Guid evaluatorEmployeeId) =>
        _assignments.Add(new ReviewAssignment(TenantId, Id, relation, evaluatorEmployeeId));

    internal void RecalculateProgress()
    {
        var completed = _assignments.Count(a => a.Status == ReviewAssignmentStatus.Completed);
        CompetencyProgress = _assignments.Count == 0 ? 0m : Math.Round(100m * completed / _assignments.Count, 2, MidpointRounding.AwayFromZero);
        if (Status == ReviewParticipantStatus.Pending && completed > 0)
        {
            Status = ReviewParticipantStatus.InProgress;
        }
    }

    internal void Complete(decimal? score, DateTime now)
    {
        CompetencyScore = score;
        FinalScore = score;
        Status = ReviewParticipantStatus.Completed;
        CompletedAtUtc = now;
        CompetencyProgress = 100m;
    }
}

/// <summary>One evaluator's form for one participant, covering every question of the cycle's active competencies.</summary>
public sealed class ReviewAssignment : TenantEntity<Guid>
{
    private readonly List<ReviewResponse> _responses = [];

    private ReviewAssignment()
    {
    }

    internal ReviewAssignment(Guid tenantId, Guid reviewParticipantId, EvaluatorRelation relation, Guid evaluatorEmployeeId)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewParticipantId = reviewParticipantId;
        Relation = relation;
        EvaluatorEmployeeId = evaluatorEmployeeId;
        Status = ReviewAssignmentStatus.Pending;
    }

    public Guid ReviewParticipantId { get; private set; }

    public EvaluatorRelation Relation { get; private set; }

    public Guid EvaluatorEmployeeId { get; private set; }

    public ReviewAssignmentStatus Status { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyList<ReviewResponse> Responses => _responses.AsReadOnly();

    internal void Complete(IReadOnlyList<(Guid QuestionId, decimal Score, string? Comment)> answers, DateTime now)
    {
        _responses.Clear();
        foreach (var answer in answers)
        {
            _responses.Add(new ReviewResponse(TenantId, Id, answer.QuestionId, answer.Score, string.IsNullOrWhiteSpace(answer.Comment) ? null : answer.Comment.Trim()));
        }

        Status = ReviewAssignmentStatus.Completed;
        CompletedAtUtc = now;
    }

    /// <summary>ForceComplete: bu atama minimum eşiğin ötesinde kaldı, raporda "Atlandı" olarak gösterilir.</summary>
    internal void Skip() => Status = ReviewAssignmentStatus.Skipped;
}

/// <summary>One evaluator's answer to one question within a <see cref="ReviewAssignment"/>.</summary>
public sealed class ReviewResponse : TenantEntity<Guid>
{
    private ReviewResponse()
    {
    }

    internal ReviewResponse(Guid tenantId, Guid reviewAssignmentId, Guid questionId, decimal score, string? comment)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewAssignmentId = reviewAssignmentId;
        QuestionId = questionId;
        Score = score;
        Comment = comment;
    }

    public Guid ReviewAssignmentId { get; private set; }

    public Guid QuestionId { get; private set; }

    public decimal Score { get; private set; }

    public string? Comment { get; private set; }
}
