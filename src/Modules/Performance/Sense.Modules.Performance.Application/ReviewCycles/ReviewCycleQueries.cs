using Sense.Modules.Performance.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

/// <summary>Admin list page ("grid standardı").</summary>
[RequiresPermission("performance.cycle.view")]
public sealed record ListReviewCyclesQuery : IQuery<IReadOnlyList<ReviewCycleSummaryDto>>;

public sealed class ListReviewCyclesHandler(IReviewCycleRepository cycles) : IQueryHandler<ListReviewCyclesQuery, IReadOnlyList<ReviewCycleSummaryDto>>
{
    public async Task<Result<IReadOnlyList<ReviewCycleSummaryDto>>> Handle(ListReviewCyclesQuery query, CancellationToken cancellationToken)
    {
        var list = await cycles.ListAsync(cancellationToken);
        IReadOnlyList<ReviewCycleSummaryDto> dtos = list.Select(ReviewCycleMapper.ToSummaryDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Değerlendirme detayı: config + katılımcı grid.</summary>
[RequiresPermission("performance.cycle.view")]
public sealed record GetReviewCycleQuery(Guid ReviewCycleId) : IQuery<ReviewCycleDto>;

public sealed class GetReviewCycleHandler(IReviewCycleRepository cycles) : IQueryHandler<GetReviewCycleQuery, ReviewCycleDto>
{
    public async Task<Result<ReviewCycleDto>> Handle(GetReviewCycleQuery query, CancellationToken cancellationToken)
    {
        var existing = await cycles.GetByIdAsync(query.ReviewCycleId, cancellationToken);
        return existing.IsFailure ? existing.Error : ReviewCycleMapper.ToDto(existing.Value);
    }
}
