using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;
using Sense.Shared.Contracts.Messaging;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Assets.Application.Assets;

public sealed record AssetCategorySummaryDto(Guid CategoryId, int Total, int Assigned);

/// <summary>
/// Inventory board counts. Unacknowledged = assigned but the assignee has not confirmed receipt. Warranty counts skip
/// Retired/Lost items; WarrantyExpiring = ends within the next <see cref="AssetSummaryHandler.WarrantyWindowDays"/>
/// days (today included), WarrantyExpired = ended before today. ReplacementOverdue = an assigned KKD item past its
/// replacement due date.
/// </summary>
public sealed record AssetSummaryDto(
    int Total, int Unassigned, int Assigned, int PendingAssignment, int Retired, int Lost,
    int Unacknowledged, int WarrantyExpiring, int WarrantyExpired, int ReplacementOverdue,
    IReadOnlyList<AssetCategorySummaryDto> Categories);

[RequiresPermission("assets.assets.view")]
public sealed record GetAssetSummaryQuery : IQuery<AssetSummaryDto>;

public sealed class AssetSummaryHandler(IAssetRepository assets, IAssetCategoryRepository categories, TimeProvider clock)
    : IQueryHandler<GetAssetSummaryQuery, AssetSummaryDto>
{
    public const int WarrantyWindowDays = 60;

    public async Task<Result<AssetSummaryDto>> Handle(GetAssetSummaryQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var windowEnd = today.AddDays(WarrantyWindowDays);
        var rows = await assets.ListSummaryRowsAsync(cancellationToken);
        var categoryById = (await categories.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var inService = rows.Where(r => r.Status is not (AssetStatus.Retired or AssetStatus.Lost)).ToList();

        return new AssetSummaryDto(
            Total: rows.Count,
            Unassigned: Count(rows, AssetStatus.Unassigned),
            Assigned: Count(rows, AssetStatus.Assigned),
            PendingAssignment: Count(rows, AssetStatus.PendingAssignment),
            Retired: Count(rows, AssetStatus.Retired),
            Lost: Count(rows, AssetStatus.Lost),
            Unacknowledged: rows.Count(r => r.Status == AssetStatus.Assigned && !r.Acknowledged),
            WarrantyExpiring: inService.Count(r => r.WarrantyEndsOn is { } ends && ends >= today && ends <= windowEnd),
            WarrantyExpired: inService.Count(r => r.WarrantyEndsOn is { } ends && ends < today),
            ReplacementOverdue: rows.Count(r =>
                r.Status == AssetStatus.Assigned
                && categoryById.GetValueOrDefault(r.CategoryId)?.ReplacementDueOn(r.IssuedAt) is { } due
                && due < today),
            Categories: rows.GroupBy(r => r.CategoryId)
                .Select(g => new AssetCategorySummaryDto(g.Key, g.Count(), g.Count(r => r.Status == AssetStatus.Assigned)))
                .OrderByDescending(c => c.Total)
                .ThenBy(c => c.CategoryId)
                .ToList());
    }

    private static int Count(IReadOnlyList<AssetSummaryRow> rows, AssetStatus status) => rows.Count(r => r.Status == status);
}
