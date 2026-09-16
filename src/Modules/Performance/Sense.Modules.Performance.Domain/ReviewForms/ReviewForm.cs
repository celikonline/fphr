using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain.ReviewForms;

/// <summary>Değerlendirme formu durumu.</summary>
public enum ReviewFormStatus
{
    Draft = 0,
    Published = 1,
}

/// <summary>
/// A weighted question on a <see cref="ReviewForm"/> (soru/ağırlık). Client-generated Guid key (Guid.CreateVersion7()
/// at add-time) - see Recruitment's identical FormField comment: without ValueGeneratedNever(), a question appended
/// to an already-tracked form in a later SaveChanges can be misclassified as Modified instead of Added.
/// </summary>
public sealed class ReviewFormQuestion
{
    private ReviewFormQuestion()
    {
    }

    internal ReviewFormQuestion(Guid id, Guid reviewFormId, string text, int weight, int order)
    {
        Id = id;
        ReviewFormId = reviewFormId;
        Text = text;
        Weight = weight;
        Order = order;
    }

    public Guid Id { get; private set; }

    public Guid ReviewFormId { get; private set; }

    public string Text { get; private set; } = string.Empty;

    /// <summary>Percentage weight (0-100); the form's question weights must sum to exactly 100 (see <see cref="ReviewForm.SetQuestions"/>).</summary>
    public int Weight { get; private set; }

    public int Order { get; private set; }
}

/// <summary>
/// A review form template (form + soru/ağırlık) belonging to a <see cref="ReviewCycles.ReviewCycle"/>. Mirrors
/// Recruitment's scorecard form template shape (weights sum to 100) but is deliberately its own aggregate: 360/1:1/
/// competency-matrix forms are later Performance packages and may need a different shape.
/// </summary>
public sealed class ReviewForm : TenantAggregateRoot<Guid>, IAuditLogged
{
    private readonly List<ReviewFormQuestion> _questions = [];

    private ReviewForm()
    {
    }

    private ReviewForm(Guid id, Guid tenantId, Guid reviewCycleId, string title)
        : base(id, tenantId)
    {
        ReviewCycleId = reviewCycleId;
        Title = title;
        Status = ReviewFormStatus.Draft;
    }

    public Guid ReviewCycleId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public ReviewFormStatus Status { get; private set; }

    public IReadOnlyList<ReviewFormQuestion> Questions => _questions.AsReadOnly();

    public static Result<ReviewForm> Create(Guid tenantId, Guid reviewCycleId, string title, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation(PerformanceDomainErrors.FormTitleRequired);
        }

        return new ReviewForm(id ?? Guid.CreateVersion7(), tenantId, reviewCycleId, Guard.MaxLength(title.Trim(), PerformanceLimits.NameMaxLength));
    }

    /// <summary>Replaces the question set wholesale (Draft only, before Publish). Weights must sum to exactly 100.</summary>
    public Result SetQuestions(IReadOnlyList<(string Text, int Weight)> questions)
    {
        if (Status != ReviewFormStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.FormNotEditable);
        }

        if (questions.Count == 0)
        {
            return Error.Validation(PerformanceDomainErrors.FormQuestionsRequired);
        }

        if (questions.Any(q => string.IsNullOrWhiteSpace(q.Text)))
        {
            return Error.Validation(PerformanceDomainErrors.FormQuestionTextRequired);
        }

        if (questions.Sum(q => q.Weight) != 100)
        {
            return Error.Validation(PerformanceDomainErrors.FormWeightsMustSumTo100);
        }

        _questions.Clear();
        var order = 0;
        foreach (var q in questions)
        {
            _questions.Add(new ReviewFormQuestion(Guid.CreateVersion7(), Id, Guard.MaxLength(q.Text.Trim(), PerformanceLimits.QuestionTextMaxLength), q.Weight, order++));
        }

        return Result.Success();
    }

    /// <summary>Draft -> Published, terminal for this template's structure (create a new form instead of editing a published one).</summary>
    public Result Publish()
    {
        if (Status != ReviewFormStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.FormNotPublishable);
        }

        if (_questions.Count == 0)
        {
            return Error.Validation(PerformanceDomainErrors.FormQuestionsRequired);
        }

        Status = ReviewFormStatus.Published;
        return Result.Success();
    }
}
