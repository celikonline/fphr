using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application;
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

    public async Task<IReadOnlyList<EmployeeEntity>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var list = ids.ToList();
        return await context.Employees.Where(e => list.Contains(e.Id)).ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AnyAsync(e => e.Id == employeeId && e.Status != EmployeeStatus.Inactive, cancellationToken);

    public async Task<bool> NationalIdTakenAsync(string nationalId, Guid? exceptEmployeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AnyAsync(e => e.NationalId == nationalId && (exceptEmployeeId == null || e.Id != exceptEmployeeId), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid?>> GetManagerMapAsync(CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.ManagerEmployeeId })
            .ToDictionaryAsync(e => e.Id, e => e.ManagerEmployeeId, cancellationToken);
}

/// <summary>Saves the Employee DbContext directly (SaveChanges is atomic: all pending changes or none).</summary>
public sealed class EmployeeUnitOfWork(EmployeeDbContext context) : IEmployeeUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}
