using Sense.Modules.Performance.Domain;
using Sense.Modules.Performance.Domain.ReviewCycles;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Performance.Application.ReviewCycles;

public sealed record ReviewCycleDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ReviewCycleStatus Status);

/// <summary>Lists the tenant's review cycles, optionally filtered to one status.</summary>
[RequiresPermission("performance.reviews.view")]
public sealed record ListReviewCyclesQuery(string? Status) : IQuery<IReadOnlyList<ReviewCycleDto>>;

public sealed class ListReviewCyclesHandler(IReviewCycleRepository cycles) : IQueryHandler<ListReviewCyclesQuery, IReadOnlyList<ReviewCycleDto>>
{
    public async Task<Result<IReadOnlyList<ReviewCycleDto>>> Handle(ListReviewCyclesQuery query, CancellationToken cancellationToken)
    {
        ReviewCycleStatus? status = string.IsNullOrWhiteSpace(query.Status) ? null : Enum.Parse<ReviewCycleStatus>(query.Status, ignoreCase: true);
        var list = await cycles.ListAsync(status, cancellationToken);
        IReadOnlyList<ReviewCycleDto> dtos = list.Select(ReviewCycleMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>A single review cycle by id.</summary>
[RequiresPermission("performance.reviews.view")]
public sealed record GetReviewCycleQuery(Guid ReviewCycleId) : IQuery<ReviewCycleDto>;

public sealed class GetReviewCycleHandler(IReviewCycleRepository cycles) : IQueryHandler<GetReviewCycleQuery, ReviewCycleDto>
{
    public async Task<Result<ReviewCycleDto>> Handle(GetReviewCycleQuery query, CancellationToken cancellationToken)
    {
        var found = await cycles.GetByIdAsync(query.ReviewCycleId, cancellationToken);
        return found.IsFailure ? found.Error : ReviewCycleMapper.ToDto(found.Value);
    }
}

/// <summary>
/// Self-service "Değerlendirmelerim": the review cycles an employee can see are running/finished (Draft cycles are
/// unannounced, excluded). No individual review-submission entity exists yet (later package) - this is the minimal
/// slice that gives performance.my.reviews an actual reader today.
/// </summary>
[RequiresPermission("performance.my.reviews")]
public sealed record ListMyReviewCyclesQuery : IQuery<IReadOnlyList<ReviewCycleDto>>;

public sealed class ListMyReviewCyclesHandler(IReviewCycleRepository cycles) : IQueryHandler<ListMyReviewCyclesQuery, IReadOnlyList<ReviewCycleDto>>
{
    public async Task<Result<IReadOnlyList<ReviewCycleDto>>> Handle(ListMyReviewCyclesQuery query, CancellationToken cancellationToken)
    {
        var list = await cycles.ListAsync(status: null, cancellationToken);
        IReadOnlyList<ReviewCycleDto> dtos = list.Where(c => c.Status != ReviewCycleStatus.Draft).Select(ReviewCycleMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

internal static class ReviewCycleMapper
{
    public static ReviewCycleDto ToDto(ReviewCycle cycle) => new(cycle.Id, cycle.Name, cycle.StartDate, cycle.EndDate, cycle.Status);
}
