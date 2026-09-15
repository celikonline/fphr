using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeLookup so other modules (e.g. Organization) can validate an employee reference.</summary>
public sealed class EmployeeLookup(EmployeeDbContext context) : IEmployeeLookup
{
    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AnyAsync(
            e => e.TenantId == tenantId && e.Id == employeeId && e.Status != EmployeeStatus.Terminated,
            cancellationToken);

    public async Task<bool> AnyActiveInOrgUnitsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken cancellationToken = default)
    {
        if (orgUnitIds.Count == 0)
        {
            return false;
        }

        var units = orgUnitIds.ToList();
        return await context.Employees.AnyAsync(
            e => e.TenantId == tenantId && e.OrgUnitId != null && units.Contains(e.OrgUnitId.Value) && e.Status != EmployeeStatus.Terminated,
            cancellationToken);
    }

    public async Task<Guid?> GetOrgUnitIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.OrgUnitId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> GetManagerEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.ManagerEmployeeId)
            .FirstOrDefaultAsync(cancellationToken);
}
