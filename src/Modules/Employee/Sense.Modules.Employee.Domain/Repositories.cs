using Sense.Modules.Employee.Domain.CustomFields;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Domain.Subcontractors;
using Sense.Modules.Employee.Domain.Terminations;
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

    /// <summary>Tracked employee with its employment periods loaded (not auto-included anywhere else); required by the termination/rehire/seniority paths.</summary>
    Task<Result<EmployeeEntity>> GetWithEmploymentPeriodsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Tracked employee with its union memberships loaded (special-category data, never auto-included).</summary>
    Task<Result<EmployeeEntity>> GetWithUnionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeEntity>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracked employees by id with their salary records and custom field values loaded (import row handler only:
    /// a batch may add salary history or custom field values to any updated employee, and both collections are
    /// otherwise not auto-included - see <see cref="GetWithPaymentsAsync"/>/<see cref="GetWithCustomFieldsAsync"/>).
    /// </summary>
    Task<IReadOnlyList<EmployeeEntity>> ListByIdsWithPaymentsAndCustomFieldsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<bool> ExistsActiveAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>True if another employee of the tenant already uses this national id.</summary>
    Task<bool> NationalIdTakenAsync(string nationalId, Guid? exceptEmployeeId, CancellationToken cancellationToken = default);

    /// <summary>Every employee id of the tenant mapped to its current manager (hierarchy checks).</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> GetManagerMapAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Headcount of employees in <see cref="EmployeeStatus.Active"/> status (Draft/Suspended/Terminated not counted) -
    /// same definition as the "Çalışan Dağılımı" widget. Feeds the job-security scope check's headcount approximation
    /// (JobSecurityEvaluator; no iş kolu/business-line grouping exists yet, see its own TODO).
    /// </summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Terminated employees whose termination date falls in [from, to] and whose termination type is one of <paramref name="types"/> (collective dismissal window count).</summary>
    Task<int> CountTerminationsAsync(
        DateOnly from, DateOnly to, IReadOnlyCollection<TerminationType> types, CancellationToken cancellationToken = default);
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

/// <summary>Termination reason catalog repository (tenant-scoped, system-seeded; see <see cref="TerminationReason"/> for why CRUD is out of scope this pass).</summary>
public interface ITerminationReasonRepository
{
    void Add(TerminationReason reason);

    Task<Result<TerminationReason>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Reasons ordered by SortOrder then Name; inactive ones included only when asked.</summary>
    Task<IReadOnlyList<TerminationReason>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>True once the tenant's catalog has been seeded (any row at all).</summary>
    Task<bool> AnyForTenantAsync(CancellationToken cancellationToken = default);

    /// <summary>True if another reason of the tenant already uses this code.</summary>
    Task<bool> CodeTakenAsync(string code, Guid? exceptId, CancellationToken cancellationToken = default);
}

/// <summary>Subcontractor ("alt işveren") catalog repository; rows are deactivated, never deleted (employees reference them).</summary>
public interface ISubcontractorRepository
{
    void Add(Subcontractor subcontractor);

    Task<Result<Subcontractor>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Subcontractors ordered by name; inactive ones included only when asked.</summary>
    Task<IReadOnlyList<Subcontractor>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>Names of the given subcontractors (card/list display).</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
