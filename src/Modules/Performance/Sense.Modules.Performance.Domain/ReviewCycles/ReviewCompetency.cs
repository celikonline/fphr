using Sense.Shared.Kernel.Domain;

namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>A competency being scored in a cycle (Yetkinlik adımı), owning its questions.</summary>
public sealed class ReviewCompetency : TenantEntity<Guid>
{
    private readonly List<ReviewQuestion> _questions = [];

    private ReviewCompetency()
    {
    }

    internal ReviewCompetency(Guid tenantId, Guid reviewCycleId, string name, decimal? weight)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewCycleId = reviewCycleId;
        Name = name;
        Weight = weight;
    }

    public Guid ReviewCycleId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Null: 100/n eşit dağıtım devreye girer (bkz. ScoreCalculator).</summary>
    public decimal? Weight { get; private set; }

    public IReadOnlyList<ReviewQuestion> Questions => _questions.AsReadOnly();

    internal void AddQuestion(Guid tenantId, int order, string text, decimal? weight, CommentMode? commentMode) =>
        _questions.Add(new ReviewQuestion(tenantId, Id, order, text, weight, commentMode));
}

/// <summary>A single rated question under a competency (Sorular adımı).</summary>
public sealed class ReviewQuestion : TenantEntity<Guid>
{
    private ReviewQuestion()
    {
    }

    internal ReviewQuestion(Guid tenantId, Guid reviewCompetencyId, int order, string text, decimal? weight, CommentMode? commentMode)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewCompetencyId = reviewCompetencyId;
        Order = order;
        Text = text;
        Weight = weight;
        CommentMode = commentMode;
    }

    public Guid ReviewCompetencyId { get; private set; }

    public int Order { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Null: 100/n eşit dağıtım devreye girer (bkz. ScoreCalculator).</summary>
    public decimal? Weight { get; private set; }

    /// <summary>Null: ReviewCycle.DefaultCommentMode geçerli olur.</summary>
    public CommentMode? CommentMode { get; private set; }
}

/// <summary>Per-relation config for a cycle (Yetkinlik adımı: aktif/pasif + ağırlık; Erişim adımı: min değerlendirici sayısı).</summary>
public sealed class ReviewRelationConfig : TenantEntity<Guid>
{
    private ReviewRelationConfig()
    {
    }

    internal ReviewRelationConfig(Guid reviewCycleId, Guid tenantId, EvaluatorRelation relation, decimal? weight, int? minRequired)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewCycleId = reviewCycleId;
        Relation = relation;
        Weight = weight;
        MinRequired = minRequired;
    }

    public Guid ReviewCycleId { get; private set; }

    public EvaluatorRelation Relation { get; private set; }

    /// <summary>Null: 100/n eşit dağıtım devreye girer (bkz. ScoreCalculator).</summary>
    public decimal? Weight { get; private set; }

    /// <summary>Null = kategori pasif. Kendisi için yalnız null veya 1; diğerleri için null veya &gt;=1.</summary>
    public int? MinRequired { get; private set; }
}
