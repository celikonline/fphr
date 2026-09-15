using Sense.Shared.Kernel.Results;
// Alias uses a name other than "Employee": a using-alias literally named "Employee" does not win
// over the enclosing Sense.Modules.Employee namespace segment (CS0118) regardless of where the
// using directive is placed, because namespace-member lookup at an outer level short-circuits
// before an inner using-alias is consulted.
using EmployeeEntity = Sense.Modules.Employee.Domain.Employees.Employee;

namespace Sense.Modules.Employee.Domain;

/// <summary>Employee aggregate repository.</summary>
public interface IEmployeeRepository
{
    void Add(EmployeeEntity employee);

    Task<Result<EmployeeEntity>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeEntity>> ListByTenantAsync(int skip, int take, CancellationToken cancellationToken = default);

    Task<int> CountByTenantAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default);
}
