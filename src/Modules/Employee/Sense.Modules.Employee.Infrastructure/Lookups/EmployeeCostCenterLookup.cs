using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

public sealed class EmployeeCostCenterLookup(EmployeeDbContext context) : IEmployeeCostCenterLookup
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0) return new Dictionary<Guid, Guid>();
        // Background consumers supply the tenant explicitly. Keep the soft-delete filter in place.
        return await context.Employees.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).AsNoTracking().IgnoreAutoIncludes()
            .Where(employee => employee.TenantId == tenantId && employeeIds.Contains(employee.Id) && employee.CostCenterId != null)
            .ToDictionaryAsync(employee => employee.Id, employee => employee.CostCenterId!.Value, cancellationToken);
    }
}
