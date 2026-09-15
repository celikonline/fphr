namespace Sense.Modules.Employee.Contracts;

/// <summary>Derived booleans only - never exposes raw BirthDate/DisabilityDegree across the module boundary (both are "hassas"/sensitive fields).</summary>
public sealed record EmployeeDocumentEligibilitySignals(bool IsUnder18, bool HasDisability);

/// <summary>
/// Read-only cross-module lookup consumed by Documents (document requirement evaluation at hire time and on manual
/// reevaluation). Implemented by Sense.Modules.Employee.Infrastructure and registered by EmployeeModule.AddModule.
/// </summary>
public interface IEmployeeDocumentEligibilityLookup
{
    /// <summary>Null if the employee doesn't exist.</summary>
    Task<EmployeeDocumentEligibilitySignals?> GetSignalsAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}
