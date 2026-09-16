using Sense.Modules.Assets.Domain.Assets;

namespace Sense.Modules.Assets.Application.Assets;

public sealed record AssetDto(
    Guid Id, Guid CategoryId, string Name, string? SerialNumber, string? Description, AssetStatus Status,
    Guid? AssignedEmployeeId, DateOnly? IssuedAt, DateOnly? ReturnedAt);

internal static class AssetMapper
{
    public static AssetDto ToDto(Asset asset) =>
        new(
            asset.Id, asset.CategoryId, asset.Name, asset.SerialNumber, asset.Description, asset.Status,
            asset.AssignedEmployeeId, asset.IssuedAt, asset.ReturnedAt);
}
