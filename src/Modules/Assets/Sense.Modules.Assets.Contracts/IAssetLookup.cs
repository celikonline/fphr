namespace Sense.Modules.Assets.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets other modules (e.g. Documents, to materialize the AssetHandoverForm
/// conditional requirement) ask whether an employee currently holds any assigned equipment, without depending on
/// Assets' Domain or Infrastructure. Implemented by Sense.Modules.Assets.Infrastructure and registered by
/// AssetsModule.AddModule, mirroring Employee.Contracts.IEmployeeLookup's style.
/// </summary>
public interface IAssetLookup
{
    /// <summary>True if the employee currently has at least one Assigned (unreturned) asset.</summary>
    Task<bool> HasOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}
