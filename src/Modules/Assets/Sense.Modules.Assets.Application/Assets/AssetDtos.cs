using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>
/// ReplacementDueOn: for an assigned item of a periodically replaced (KKD) category, IssuedAt + the interval.
/// AcknowledgedAt: only while assigned - when the assignee confirmed receipt of the open assignment (null until then).
/// </summary>
public sealed record AssetDto(
    Guid Id, Guid CategoryId, string Name, string? SerialNumber, string? Description, AssetStatus Status,
    Guid? AssignedEmployeeId, DateOnly? IssuedAt, DateOnly? ReturnedAt, DateOnly? ReplacementDueOn,
    string? AssetTag = null, string? Brand = null, string? Model = null, DateOnly? PurchaseDate = null, decimal? PurchaseCost = null,
    DateOnly? WarrantyEndsOn = null, AssetCondition Condition = AssetCondition.Good, DateTime? AcknowledgedAt = null);

/// <summary>One row of an asset's assignment ledger.</summary>
public sealed record AssetAssignmentDto(
    Guid Id, Guid EmployeeId, DateOnly IssuedAt, DateOnly? ReturnedAt, AssignmentEndReason? EndReason,
    AssetCondition? ReturnCondition, string? Note, DateTime? AcknowledgedAt);

/// <summary>An employee's ledger row plus the asset it was for (employee-side history).</summary>
public sealed record EmployeeAssetHistoryDto(
    Guid Id, Guid EmployeeId, DateOnly IssuedAt, DateOnly? ReturnedAt, AssignmentEndReason? EndReason,
    AssetCondition? ReturnCondition, string? Note, DateTime? AcknowledgedAt,
    Guid AssetId, string AssetName, Guid CategoryId, string? SerialNumber, string? AssetTag);

internal static class AssetMapper
{
    /// <summary>Loads the tenant's categories once (a handful of rows) so each asset gets its replacement due date.</summary>
    public static async Task<Func<Asset, AssetDto>> CreateAsync(IAssetCategoryRepository categories, CancellationToken cancellationToken)
    {
        var byId = (await categories.ListAsync(cancellationToken)).ToDictionary(c => c.Id);
        return asset => ToDto(asset, byId.GetValueOrDefault(asset.CategoryId));
    }

    public static AssetDto ToDto(Asset asset, AssetCategory? category) =>
        new(
            asset.Id, asset.CategoryId, asset.Name, asset.SerialNumber, asset.Description, asset.Status,
            asset.AssignedEmployeeId, asset.IssuedAt, asset.ReturnedAt,
            asset.Status == AssetStatus.Assigned ? category?.ReplacementDueOn(asset.IssuedAt) : null,
            asset.AssetTag, asset.Brand, asset.Model, asset.PurchaseDate, asset.PurchaseCost, asset.WarrantyEndsOn,
            asset.Condition,
            asset.Status == AssetStatus.Assigned ? asset.OpenAssignment?.AcknowledgedAt : null);

    public static AssetAssignmentDto ToDto(AssetAssignment a) =>
        new(a.Id, a.EmployeeId, a.IssuedAt, a.ReturnedAt, a.EndReason, a.ReturnCondition, a.Note, a.AcknowledgedAt);

    public static EmployeeAssetHistoryDto ToDto(AssetAssignmentHistoryItem item) =>
        new(
            item.Assignment.Id, item.Assignment.EmployeeId, item.Assignment.IssuedAt, item.Assignment.ReturnedAt,
            item.Assignment.EndReason, item.Assignment.ReturnCondition, item.Assignment.Note, item.Assignment.AcknowledgedAt,
            item.Asset.Id, item.Asset.Name, item.Asset.CategoryId, item.Asset.SerialNumber, item.Asset.AssetTag);
}
