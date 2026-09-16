using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewForms;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewForms;

public sealed record ReviewFormQuestionDto(Guid Id, string Text, int Weight, int Order);

public sealed record ReviewFormDto(Guid Id, Guid ReviewCycleId, string Title, ReviewFormStatus Status, IReadOnlyList<ReviewFormQuestionDto> Questions);

/// <summary>Lists a review cycle's forms.</summary>
[RequiresPermission("performance.reviews.view")]
public sealed record ListReviewFormsQuery(Guid ReviewCycleId) : IQuery<IReadOnlyList<ReviewFormDto>>;

public sealed class ListReviewFormsHandler(IReviewFormRepository forms) : IQueryHandler<ListReviewFormsQuery, IReadOnlyList<ReviewFormDto>>
{
    public async Task<Result<IReadOnlyList<ReviewFormDto>>> Handle(ListReviewFormsQuery query, CancellationToken cancellationToken)
    {
        var list = await forms.ListByCycleAsync(query.ReviewCycleId, cancellationToken);
        IReadOnlyList<ReviewFormDto> dtos = list.Select(ReviewFormMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class ReviewFormMapper
{
    public static ReviewFormDto ToDto(ReviewForm form) => new(
        form.Id,
        form.ReviewCycleId,
        form.Title,
        form.Status,
        form.Questions.OrderBy(q => q.Order).Select(q => new ReviewFormQuestionDto(q.Id, q.Text, q.Weight, q.Order)).ToList());
}
