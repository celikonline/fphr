using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>
/// Implements Organization.Contracts.IEmployeeCostCenterReader so Payroll (and other modules) can
/// resolve employee-to-cost-center assignments during export/filtering (Faz 7 tur 1 ACİL KARAR, ADR-0023).
/// Currently returns an empty dictionary because Employee.Domain.Employee does not yet have a CostCenterId field.
/// Once Employee.Domain adds CostCenterId and the associated migration, this query will fetch real assignments.
/// </summary>
public sealed class EmployeeCostCenterReader(OrganizationDbContext context) : IEmployeeCostCenterReader
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetEmployeeCostCenterMappingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        // TODO: Once Employee.Domain.Employee.CostCenterId field exists in a future release,
        // this will fetch real assignments via OrgUnit or direct employee field.
        // Expected query (when Employee has CostCenterId):
        // var result = await context.Employees
        //     .Where(e => e.TenantId == tenantId && employeeIds.Contains(e.Id) && e.CostCenterId != null)
        //     .Select(e => new { e.Id, e.CostCenterId })
        //     .ToDictionaryAsync(e => e.Id, e => e.CostCenterId!.Value, cancellationToken);
        // return result;

        // For now, no employees have cost center assignments.
        return new Dictionary<Guid, Guid>();
    }
}
