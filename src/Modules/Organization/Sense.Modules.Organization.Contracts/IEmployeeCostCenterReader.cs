namespace Sense.Modules.Organization.Contracts;

/// <summary>
/// Read-only cross-module lookup for employee-to-cost-center assignments (faz-07 tur 1 ACİL KARAR,
/// ADR-0023). Lets Payroll and other modules filter/allocate based on employee's assigned cost center
/// without depending on Organization's Domain or Infrastructure. Only returns employees with a
/// cost center assignment; employees without an assignment are omitted from the result.
/// Implemented by Sense.Modules.Organization.Infrastructure and registered by
/// OrganizationModule.AddModule.
/// </summary>
public interface IEmployeeCostCenterReader
{
    /// <summary>
    /// Returns a mapping of employee IDs to their assigned cost center IDs. Only includes employees
    /// that have a cost center assignment; employees without an assignment are omitted from the dictionary.
    /// </summary>
    /// <param name="tenantId">The tenant ID for isolation.</param>
    /// <param name="employeeIds">The employee IDs to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary where keys are employee IDs and values are their assigned cost center IDs.
    /// Employees without a cost center assignment are not included in the result.</returns>
    Task<IReadOnlyDictionary<Guid, Guid>> GetEmployeeCostCenterMappingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken = default);
}
