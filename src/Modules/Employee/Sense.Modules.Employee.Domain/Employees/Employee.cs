using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Employee.Domain.Employees;

/// <summary>Lifecycle status of a minimal employee stub. Full status/lifecycle modeling is Faz 1 scope.</summary>
public enum EmployeeStatus
{
    Active = 0,
    Inactive = 1,
    OnLeave = 2,
}

/// <summary>
/// Faz 0 minimal employee stub (roadmap item #4): just enough to be referenced by
/// Organization.OrgUnit.ManagerEmployeeId and by later roadmap items (e.g. unit authorization). Full
/// personal/contract/career record (career history, contract type, documents, ...) is explicitly out
/// of scope here - that is Faz 1's "özlük" work. <see cref="UserId"/> and <see cref="OrgUnitId"/> are
/// plain cross-module identifiers - no EF navigation property, no DB foreign key - resolved at the
/// Application layer via each owning module's Contracts lookup (Identity for UserId is out of scope
/// for validation here since Identity.User already carries the inverse EmployeeId link and performs
/// its own linking; Organization.Contracts.IOrgUnitLookup validates OrgUnitId).
/// </summary>
public sealed class Employee : TenantAggregateRoot<Guid>, IAuditLogged
{
    private Employee()
    {
    }

    private Employee(Guid id, Guid tenantId, string displayName, Guid? userId, Guid? orgUnitId)
        : base(id, tenantId)
    {
        DisplayName = displayName;
        UserId = userId;
        OrgUnitId = orgUnitId;
        Status = EmployeeStatus.Active;
    }

    /// <summary>Cross-module reference to Identity.User; nullable because an employee record can exist before a login account (e.g. bulk özlük import ahead of inviting the user).</summary>
    public Guid? UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public EmployeeStatus Status { get; private set; }

    /// <summary>Cross-module reference to Organization.OrgUnit; nullable (an employee may not be assigned to a unit yet).</summary>
    public Guid? OrgUnitId { get; private set; }

    public static Result<Employee> Create(Guid tenantId, string displayName, Guid? userId = null, Guid? orgUnitId = null, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation(EmployeeDomainErrors.DisplayNameRequired);
        }

        var employee = new Employee(
            id ?? Guid.CreateVersion7(),
            tenantId,
            Guard.MaxLength(displayName.Trim(), EmployeeLimits.DisplayNameMaxLength),
            userId,
            orgUnitId);

        employee.Raise(new EmployeeCreated(employee.Id, tenantId, employee.DisplayName, userId, orgUnitId));
        return employee;
    }

    public void Rename(string displayName) => DisplayName = Guard.MaxLength(Guard.NotEmpty(displayName), EmployeeLimits.DisplayNameMaxLength);

    public void AssignOrgUnit(Guid? orgUnitId)
    {
        OrgUnitId = orgUnitId;
        Raise(new EmployeeOrgUnitChanged(Id, TenantId, orgUnitId));
    }

    public void LinkUser(Guid userId) => UserId = userId;

    public void SetStatus(EmployeeStatus status)
    {
        Status = status;
        Raise(new EmployeeStatusChanged(Id, TenantId, status));
    }
}

public sealed record EmployeeCreated(Guid EmployeeId, Guid TenantId, string DisplayName, Guid? UserId, Guid? OrgUnitId) : DomainEvent;

public sealed record EmployeeOrgUnitChanged(Guid EmployeeId, Guid TenantId, Guid? OrgUnitId) : DomainEvent;

public sealed record EmployeeStatusChanged(Guid EmployeeId, Guid TenantId, EmployeeStatus Status) : DomainEvent;
