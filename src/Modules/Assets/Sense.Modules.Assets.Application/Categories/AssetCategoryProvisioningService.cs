using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;

namespace Sense.Modules.Assets.Application.Categories;

/// <summary>Default seed row: code + display name (tenant may rename or add more).</summary>
public sealed record AssetCategorySeedRow(string Code, string Name);

/// <summary>System-wide starting catalog for the equipment category catalog.</summary>
public static class AssetCategorySeed
{
    public static IReadOnlyList<AssetCategorySeedRow> SystemDefinitions() =>
    [
        new("computer", "Bilgisayar"),
        new("phone", "Telefon"),
        new("vehicle", "Araç"),
        new("other", "Diğer"),
    ];
}

/// <summary>
/// Seeds the tenant's category catalog the first time it's needed - mirrors Expense's ExpenseCategoryProvisioningService
/// (no per-module tenant-provisioning hook exists yet, so this runs lazily from the first read/write that needs the
/// catalog). Does not save; the caller must call its own IAssetsUnitOfWork.SaveChangesAsync afterwards.
/// </summary>
public sealed class AssetCategoryProvisioningService(IAssetCategoryRepository categories)
{
    public async Task EnsureSeededAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await categories.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var row in AssetCategorySeed.SystemDefinitions())
        {
            categories.Add(AssetCategory.Create(tenantId, row.Code, row.Name).Value);
        }
    }
}
