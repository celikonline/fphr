using Sense.Modules.Organization.Contracts;

namespace Sense.Modules.Payroll.Infrastructure.Adapters;

/// <summary>
/// Temporary stub adapter for <see cref="IEmployeeCostCenterReader"/> (faz-07 tur 1 ACİL KARAR, ADR-0023).
/// Payroll queries the organization's employee-to-cost-center assignment during period filtering. Faz 2 will
/// implement this with real data from OrgUnit/CostCenter assignments; until then, returns empty (no cost center
/// assignments yet).
/// </summary>
public sealed class EmployeeCostCenterReaderAdapter : IEmployeeCostCenterReader
{
    public Task<IReadOnlyDictionary<Guid, Guid>> GetEmployeeCostCenterMappingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken = default)
    {
        // TODO: faz-02 BE - real implementation from OrgUnit/CostCenter employee assignments.
        // For now, return empty dictionary (no employees have cost center assignments).
        return Task.FromResult((IReadOnlyDictionary<Guid, Guid>)new Dictionary<Guid, Guid>());
    }
}
