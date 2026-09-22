using Sense.Modules.Assets.Domain;
using Sense.Modules.Assets.Domain.Assets;

namespace Sense.Modules.Assets.Application.Categories;

/// <summary>Default seed row: code + display name (tenant may rename or add more).</summary>
public sealed record AssetCategorySeedRow(string Code, string Name, int? ReplacementIntervalDays = null);

/// <summary>System-wide starting catalog for the equipment category catalog.</summary>
public static class AssetCategorySeed
{
    public static IReadOnlyList<AssetCategorySeedRow> SystemDefinitions() =>
    [
        new("computer", "Bilgisayar"),
        new("phone", "Telefon"),
        new("vehicle", "Araç"),
        new("other", "Diğer"),
        // senseik-015 Faz 4: KKD zimmeti; a yearly default the tenant can change per category.
        new("ppe", "Kişisel koruyucu donanım (KKD)", 365),
    ];
}

/// <summary>
/// Seeds the tenant's category catalog the first time it's needed - mirrors Expense's ExpenseCategoryProvisioningService
/// (no per-module tenant-provisioning hook exists yet, so this runs lazily from the first read/write that needs the
/// catalog). Adds every seed code the tenant does not have yet, so a row added to the seed later (e.g. "ppe") also
/// reaches tenants seeded before it; categories are never deleted, so a missing code was never there.
/// Does not save; the caller must call its own IAssetsUnitOfWork.SaveChangesAsync afterwards.
/// </summary>
public sealed class AssetCategoryProvisioningService(IAssetCategoryRepository categories)
{
    public async Task EnsureSeededAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = (await categories.ListAsync(cancellationToken))
            .Select(c => c.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in AssetCategorySeed.SystemDefinitions().Where(r => !existing.Contains(r.Code)))
        {
            categories.Add(AssetCategory.Create(tenantId, row.Code, row.Name, replacementIntervalDays: row.ReplacementIntervalDays).Value);
        }
    }
}
