using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;

namespace Sense.Modules.Assets.Application.Assets;

/// <summary>ReplacementDueOn: for an assigned item of a periodically replaced (KKD) category, IssuedAt + the interval.</summary>
public sealed record AssetDto(
    Guid Id, Guid CategoryId, string Name, string? SerialNumber, string? Description, AssetStatus Status,
    Guid? AssignedEmployeeId, DateOnly? IssuedAt, DateOnly? ReturnedAt, DateOnly? ReplacementDueOn);

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
            asset.Status == AssetStatus.Assigned ? category?.ReplacementDueOn(asset.IssuedAt) : null);
}
