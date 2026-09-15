using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Application;
using Sense.Modules.Employee.Domain;
using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Terminations;
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

    public async Task<Result<EmployeeEntity>> GetWithPaymentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await context.Employees
            .Include(e => e.SalaryRecords)
            .Include(e => e.BankAccounts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return employee is null ? Error.NotFound(EmployeeDomainErrors.EmployeeNotFound) : employee;
    }

    public async Task<Result<EmployeeEntity>> GetWithCustomFieldsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await context.Employees
            .Include(e => e.CustomFieldValues)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return employee is null ? Error.NotFound(EmployeeDomainErrors.EmployeeNotFound) : employee;
    }

    public async Task<Result<EmployeeEntity>> GetWithEmploymentPeriodsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await context.Employees
            .Include(e => e.EmploymentPeriods)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
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
        await context.Employees.AnyAsync(e => e.Id == employeeId && e.Status != EmployeeStatus.Terminated, cancellationToken);

    public async Task<bool> NationalIdTakenAsync(string nationalId, Guid? exceptEmployeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AnyAsync(e => e.NationalId == nationalId && (exceptEmployeeId == null || e.Id != exceptEmployeeId), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Guid?>> GetManagerMapAsync(CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Select(e => new { e.Id, e.ManagerEmployeeId })
            .ToDictionaryAsync(e => e.Id, e => e.ManagerEmployeeId, cancellationToken);

    public async Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        await context.Employees.CountAsync(e => e.Status == EmployeeStatus.Active, cancellationToken);
}

/// <summary>Saves the Employee DbContext directly (SaveChanges is atomic: all pending changes or none).</summary>
public sealed class EmployeeUnitOfWork(EmployeeDbContext context) : IEmployeeUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);
}

/// <summary>EF Core-backed custom field definition repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class CustomFieldDefinitionRepository(EmployeeDbContext context) : ICustomFieldDefinitionRepository
{
    public void Add(CustomFieldDefinition definition) => context.CustomFieldDefinitions.Add(definition);

    public async Task<Result<CustomFieldDefinition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var definition = await context.CustomFieldDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? Error.NotFound(CustomFieldDomainErrors.NotFound) : definition;
    }

    public async Task<bool> KeyTakenAsync(string key, Guid? exceptId, CancellationToken cancellationToken = default) =>
        await context.CustomFieldDefinitions.AnyAsync(d => d.Key == key && (exceptId == null || d.Id != exceptId), cancellationToken);

    public async Task<IReadOnlyList<CustomFieldDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.CustomFieldDefinitions.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        return await query.OrderBy(d => d.SortOrder).ThenBy(d => d.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomFieldDefinition>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await context.CustomFieldDefinitions.AsNoTracking().Where(d => d.IsActive)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .ToListAsync(cancellationToken);
}

/// <summary>EF Core-backed termination reason repository with tenant isolation (via ModuleDbContext's global query filter).</summary>
public sealed class TerminationReasonRepository(EmployeeDbContext context) : ITerminationReasonRepository
{
    public void Add(TerminationReason reason) => context.TerminationReasons.Add(reason);

    public async Task<Result<TerminationReason>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reason = await context.TerminationReasons.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return reason is null ? Error.NotFound(EmployeeDomainErrors.TerminationReasonNotFound) : reason;
    }

    public async Task<IReadOnlyList<TerminationReason>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = context.TerminationReasons.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        return await query.OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyForTenantAsync(CancellationToken cancellationToken = default) =>
        await context.TerminationReasons.AnyAsync(cancellationToken);
}
