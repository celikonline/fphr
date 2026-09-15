using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Shared.Kernel.Results;
// Alias uses a name other than "Employee" - see Sense.Modules.Employee.Domain.Repositories.cs for why.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Infrastructure.Persistence;

/// <summary>EF Core-backed Employee repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class EmployeeRepository(EmployeeDbContext context) : IEmployeeRepository
{
    public void Add(EmployeeEntity employee) => context.Employees.Add(employee);

    public async Task<Result<EmployeeEntity>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await context.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return employee is null ? Error.NotFound(EmployeeDomainErrors.EmployeeNotFound) : employee;
    }

    public async Task<IReadOnlyList<EmployeeEntity>> ListByTenantAsync(int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Employees
            .OrderBy(e => e.DisplayName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<int> CountByTenantAsync(CancellationToken cancellationToken = default) =>
        await context.Employees.CountAsync(cancellationToken);

    public async Task<bool> ExistsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AnyAsync(e => e.Id == employeeId && e.Status != EmployeeStatus.Inactive, cancellationToken);
}
