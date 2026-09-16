using Sense.Modules.Assets.Contracts;
using Sense.Modules.Assets.Domain;

namespace Sense.Modules.Assets.Infrastructure.Lookups;

/// <summary>Implements Assets.Contracts.IAssetLookup so other modules (e.g. Documents) can read the "has open assets" signal.</summary>
public sealed class AssetLookup(IAssetRepository assets) : IAssetLookup
{
    public Task<bool> HasOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        assets.HasOpenAssetsAsync(tenantId, employeeId, cancellationToken);
}
