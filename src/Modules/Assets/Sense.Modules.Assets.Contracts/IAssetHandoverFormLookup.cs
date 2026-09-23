using Sense.Shared.Contracts.Export;

namespace Sense.Modules.Assets.Contracts;

/// <summary>
/// Read-only cross-module lookup, separate from <see cref="IAssetLookup"/> so consumers that only need the cheap
/// "has open assets" signal (e.g. Employee's offboarding wizard) don't pull in PDF rendering's own dependencies
/// (ISimplePdfWriter, Employee.Contracts.IEmployeeContactLookup). Lets Documents render the zimmet tutanağı
/// (equipment handover form) PDF for one asset - the same content as GET /assets/{id}/handover-form - to
/// auto-deliver it into the employee's Documents folder when the asset is assigned, without Documents depending on
/// Assets' Application/Infrastructure. Implemented by Sense.Modules.Assets.Infrastructure, registered by
/// AddAssetsCoreServices (so it also resolves from Sense.Worker's outbox poller).
/// </summary>
public interface IAssetHandoverFormLookup
{
    /// <summary>Null when the asset does not exist or isn't currently assigned for this tenant (mirrors
    /// GetAssetHandoverFormHandler's own not-found handling).</summary>
    Task<ExportFileResult?> RenderAsync(Guid tenantId, Guid assetId, CancellationToken cancellationToken = default);
}
