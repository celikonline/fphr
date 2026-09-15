namespace Sense.Modules.Employee.Contracts;

/// <summary>
/// Read-only cross-module lookup: lets other modules (e.g. Organization, when assigning
/// OrgUnit.ManagerEmployeeId) validate an employee reference without depending on Employee's Domain
/// or Infrastructure. Mirrors the pattern this codebase already uses for cross-module reads (see
/// Sense.Shared.Contracts.Security.IEmployeeScopeLookup). Implemented by
/// Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeLookup
{
    /// <summary>True if an active (not Inactive) employee with this id exists for the given tenant.</summary>
    Task<bool> ExistsActiveAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}
