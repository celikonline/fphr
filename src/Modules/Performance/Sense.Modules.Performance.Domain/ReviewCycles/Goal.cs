using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Domain.ReviewCycles;

/// <summary>One [Min, Max] → Points range of a goal's scale ("kaç satış yaparsa şu kadar puan"). Ranges are inclusive on both ends.</summary>
public sealed record GoalScaleRange(decimal Min, decimal Max, decimal Points);

/// <summary>
/// A participant's goal (Yetkinlik + Hedef only). Owned by <see cref="ReviewParticipant"/> - goals have no lifecycle
/// independent of the participant they were set for.
/// </summary>
public sealed class Goal : TenantEntity<Guid>
{
    private Goal()
    {
    }

    internal Goal(
        Guid tenantId, Guid reviewParticipantId, Guid categoryId, string name, string? description,
        decimal targetValue, IReadOnlyList<GoalScaleRange> scale, decimal? weight, Guid enteredBy)
        : base(Guid.CreateVersion7(), tenantId)
    {
        ReviewParticipantId = reviewParticipantId;
        CategoryId = categoryId;
        Name = name;
        Description = description;
        TargetValue = targetValue;
        Scale = scale;
        Weight = weight;
        Status = GoalStatus.Draft;
        EnteredBy = enteredBy;
    }

    public Guid ReviewParticipantId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal TargetValue { get; private set; }

    public decimal? ActualValue { get; private set; }

    public IReadOnlyList<GoalScaleRange> Scale { get; private set; } = [];

    /// <summary>Gerçekleşenin düştüğü skala aralığının puanı - <see cref="EnterResult"/> sırasında hesaplanır.</summary>
    public decimal? Points { get; private set; }

    public string? ResultComment { get; private set; }

    /// <summary>Null: 100/n eşit dağıtım devreye girer (katılımcının hedefleri arası).</summary>
    public decimal? Weight { get; private set; }

    public GoalStatus Status { get; private set; }

    public Guid EnteredBy { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public Guid? ResultEnteredBy { get; private set; }

    public Guid? ResultApprovedBy { get; private set; }

    internal Result Approve(Guid approvedBy)
    {
        if (Status != GoalStatus.Draft)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotDraft);
        }

        Status = GoalStatus.Approved;
        ApprovedBy = approvedBy;
        return Result.Success();
    }

    internal Result EnterResult(decimal actualValue, string? resultComment, Guid enteredBy)
    {
        if (Status != GoalStatus.Approved)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotApproved);
        }

        var range = Scale.FirstOrDefault(r => actualValue >= r.Min && actualValue <= r.Max);
        if (range is null)
        {
            return Error.Validation(PerformanceDomainErrors.GoalResultOutOfScale, (nameof(actualValue), actualValue));
        }

        ActualValue = actualValue;
        Points = range.Points;
        ResultComment = string.IsNullOrWhiteSpace(resultComment) ? null : Guard.MaxLength(resultComment.Trim(), PerformanceLimits.CommentMaxLength);
        Status = GoalStatus.ResultEntered;
        ResultEnteredBy = enteredBy;
        return Result.Success();
    }

    internal Result ApproveResult(Guid approvedBy)
    {
        if (Status != GoalStatus.ResultEntered)
        {
            return Error.Rule(PerformanceDomainErrors.GoalNotApproved);
        }

        Status = GoalStatus.ResultApproved;
        ResultApprovedBy = approvedBy;
        return Result.Success();
    }
}
