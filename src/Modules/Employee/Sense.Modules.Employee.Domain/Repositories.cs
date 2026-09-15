using Sense.Modules.Employee.Domain.CustomFields;
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

    /// <summary>Tracked employee with its salary records and bank accounts loaded (not auto-included anywhere else).</summary>
    Task<Result<EmployeeEntity>> GetWithPaymentsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tracked employee with its custom field values loaded (not auto-included anywhere else).</summary>
    Task<Result<EmployeeEntity>> GetWithCustomFieldsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeEntity>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>True if another employee of the tenant already uses this national id.</summary>
    Task<bool> NationalIdTakenAsync(string nationalId, Guid? exceptEmployeeId, CancellationToken cancellationToken = default);

    /// <summary>Every employee id of the tenant mapped to its current manager (hierarchy checks).</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> GetManagerMapAsync(CancellationToken cancellationToken = default);
}

/// <summary>Custom field definition repository (tenant admin catalog; see <see cref="CustomFieldDefinition"/> for why it is never hard-deleted).</summary>
public interface ICustomFieldDefinitionRepository
{
    void Add(CustomFieldDefinition definition);

    Task<Result<CustomFieldDefinition>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True if another definition of the tenant already uses this key.</summary>
    Task<bool> KeyTakenAsync(string key, Guid? exceptId, CancellationToken cancellationToken = default);

    /// <summary>All definitions (admin catalog, includes inactive when requested), ordered by SortOrder then Name.</summary>
    Task<IReadOnlyList<CustomFieldDefinition>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Active definitions only - the set employee custom field values are validated/rendered against.</summary>
    Task<IReadOnlyList<CustomFieldDefinition>> ListActiveAsync(CancellationToken cancellationToken = default);
}
