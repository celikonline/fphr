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
            e => e.TenantId == tenantId && e.Id == employeeId && e.Status != EmployeeStatus.Inactive,
            cancellationToken);
}
