namespace Sense.Modules.Organization.Contracts;

/// <summary>Read-only summary of a cost center, returned by <see cref="ICostCenterLookup"/>. No sensitive data - safe for cross-module consumption (Payroll/Analytics, S-4).</summary>
public sealed record CostCenterSummary(Guid Id, string Code, string Name, bool IsActive);

/// <summary>
/// Read-only cross-module lookup for CostCenter (roadmap Faz 2 item #2, S-4), mirroring <see cref="ILocationLookup"/>.
/// Lets other modules (Payroll cost allocation, Analytics reporting) resolve a CostCenterId reference without depending on Organization's Domain or
/// Infrastructure. Implemented by Sense.Modules.Organization.Infrastructure and registered by
/// OrganizationModule.AddModule.
/// </summary>
public interface ICostCenterLookup
{
    /// <summary>Returns the cost center's summary, or null if it doesn't exist for the given tenant.</summary>
    Task<CostCenterSummary?> FindAsync(Guid tenantId, Guid costCenterId, CancellationToken cancellationToken = default);
}
