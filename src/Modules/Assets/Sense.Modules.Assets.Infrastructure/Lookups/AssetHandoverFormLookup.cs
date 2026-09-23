using Sense.Modules.Assets.Application;
using Sense.Modules.Assets.Contracts;
using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Assets.Infrastructure.Lookups;

/// <summary>Implements Assets.Contracts.IAssetHandoverFormLookup by delegating to the same IAssetHandoverFormProvider
/// the Assets API's own GET /assets/{id}/handover-form endpoint uses.</summary>
public sealed class AssetHandoverFormLookup(IAssetHandoverFormProvider handoverForms) : IAssetHandoverFormLookup
{
    public Task<ExportFileResult?> RenderAsync(Guid tenantId, Guid assetId, CancellationToken cancellationToken = default) =>
        handoverForms.RenderPdfAsync(tenantId, assetId, cancellationToken);
}
