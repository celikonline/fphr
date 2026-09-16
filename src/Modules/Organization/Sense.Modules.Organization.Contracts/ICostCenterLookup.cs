namespace Sense.Modules.Organization.Contracts;

/// <summary>Read-only summary of a cost center, returned by <see cref="ICostCenterLookup"/>. No sensitive data - safe for cross-module consumption (Payroll/Analytics, S-4).</summary>
public sealed record CostCenterSummary(Guid Id, string Code, string Name, bool IsActive);

/// <summary>
/// Read-only cross-module lookup for CostCenter (roadmap Faz 2 item #2, S-4), mirroring <see cref="ILocationLookup"/>.
/// Lets other modules (Payroll cost allocation, Analytics reporting - not wired yet, see TALIMAT tur 3 md.5
/// "başka modüle bağlama") resolve a CostCenterId reference without depending on Organization's Domain or
/// Infrastructure. Implemented by Sense.Modules.Organization.Infrastructure and registered by
/// OrganizationModule.AddModule.
/// <b>Not implemented yet</b> (Infrastructure/Lookups/CostCenterLookup.cs + DI registration land together
/// with the CostCenter EF config/migration - see MIGRATION-ISTEGI.md for why that's deferred). This
/// interface exists now purely as the contract-first shape other modules can code against/mock ahead of time.
/// </summary>
public interface ICostCenterLookup
{
    /// <summary>Returns the cost center's summary, or null if it doesn't exist for the given tenant.</summary>
    Task<CostCenterSummary?> FindAsync(Guid tenantId, Guid costCenterId, CancellationToken cancellationToken = default);
}
