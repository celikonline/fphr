namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets other modules (e.g. Organization, when assigning
/// OrgUnit.ManagerEmployeeId) validate an employee reference without depending on Employee's Domain
/// or Infrastructure. Implemented by Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeLookup
{
    /// <summary>True if an active (not Inactive) employee with this id exists for the given tenant.</summary>
    Task<bool> ExistsActiveAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>True if any active (not Inactive) employee is assigned to one of the given org units.</summary>
    Task<bool> AnyActiveInOrgUnitsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken cancellationToken = default);

    /// <summary>The employee's org unit, or null if the employee does not exist or has no unit.</summary>
    Task<Guid?> GetOrgUnitIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>The employee's direct manager, or null if the employee does not exist or has no manager assigned.</summary>
    Task<Guid?> GetManagerEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The single active employee whose national id matches exactly (import row matching, e.g. Attendance's PDKS
    /// template); null when no active employee has this national id. National id is stored unmasked for this reason.
    /// </summary>
    Task<Guid?> FindActiveByNationalIdAsync(Guid tenantId, string nationalId, CancellationToken cancellationToken = default);
}
