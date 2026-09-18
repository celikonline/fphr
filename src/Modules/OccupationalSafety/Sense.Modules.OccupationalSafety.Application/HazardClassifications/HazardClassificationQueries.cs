using Sense.Modules.OccupationalSafety.Domain;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.OccupationalSafety.Application.HazardClassifications;

/// <summary>One row per location that has ever been classified: the classification in effect today.</summary>
[RequiresPermission("ohs.hazard_classifications.view")]
public sealed record ListCurrentHazardClassificationsQuery : IQuery<IReadOnlyList<HazardClassificationDto>>;

public sealed class ListCurrentHazardClassificationsHandler(IWorkplaceHazardClassificationRepository classifications, TimeProvider clock)
    : IQueryHandler<ListCurrentHazardClassificationsQuery, IReadOnlyList<HazardClassificationDto>>
{
    public async Task<Result<IReadOnlyList<HazardClassificationDto>>> Handle(ListCurrentHazardClassificationsQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var current = await classifications.ListCurrentForAllLocationsAsync(today, cancellationToken);
        IReadOnlyList<HazardClassificationDto> dtos = current.Select(HazardClassificationMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}

/// <summary>Full reclassification history for one location, newest EffectiveFrom first.</summary>
[RequiresPermission("ohs.hazard_classifications.view")]
public sealed record ListHazardClassificationHistoryQuery(Guid LocationId) : IQuery<IReadOnlyList<HazardClassificationDto>>;

public sealed class ListHazardClassificationHistoryHandler(IWorkplaceHazardClassificationRepository classifications)
    : IQueryHandler<ListHazardClassificationHistoryQuery, IReadOnlyList<HazardClassificationDto>>
{
    public async Task<Result<IReadOnlyList<HazardClassificationDto>>> Handle(ListHazardClassificationHistoryQuery query, CancellationToken cancellationToken)
    {
        var history = await classifications.ListHistoryAsync(query.LocationId, cancellationToken);
        IReadOnlyList<HazardClassificationDto> dtos = history.Select(HazardClassificationMapper.ToDto).ToList();
        return Result.Success(dtos);
    }
}
