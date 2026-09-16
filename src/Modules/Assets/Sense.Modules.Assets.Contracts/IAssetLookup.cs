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

    /// <summary>
    /// Count of the employee's currently Assigned (unreturned) assets. Used by Employee's offboarding wizard
    /// (TerminateEmployeeCommand) to show a non-blocking "N zimmet iade edilmedi" warning - termination is never
    /// gated on this, only advisory (mirrors JobSecurityEvaluator's warning-not-block pattern).
    /// </summary>
    Task<int> CountOpenAssetsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}
