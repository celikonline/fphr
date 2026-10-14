using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.OrgUnits;

/// <summary>
/// A node in the tenant's org hierarchy. Depth is arbitrary via <see cref="ParentId"/> (self-reference)
/// rather than a fixed Company/Branch/Department/Title shape; the unit's level comes from
/// <see cref="TypeId"/> (an OrgUnitType row), so tenants can add/remove/reorder levels (Faz 0 madde 4).
/// <see cref="ManagerEmployeeId"/> and <see cref="LocationId"/> are plain cross-module identifiers -
/// no EF navigation property, no DB foreign key - resolved at the Application layer via each owning
/// module's Contracts lookup (see Sense.Modules.Employee.Contracts.IEmployeeLookup). LocationId points
/// at a Location entity that a different roadmap item still needs to build; the column is reserved
/// ahead of time so this item does not need to be revisited for that FK to exist.
/// </summary>
public sealed class OrgUnit : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OrgUnit()
    {
    }

    private OrgUnit(Guid id, Guid tenantId, Guid typeId, Guid? parentId, string name, int sortOrder)
        : base(id, tenantId)
    {
        TypeId = typeId;
        ParentId = parentId;
        Name = name;
        SortOrder = sortOrder;
        IsActive = true;
    }

    /// <summary>Which hierarchy level (OrgUnitType) this unit belongs to.</summary>
    public Guid TypeId { get; private set; }

    /// <summary>Parent unit, one level up; null for a root unit (the Company level has no parent).</summary>
    public Guid? ParentId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Cross-module reference to Employee.Employee; nullable (a unit can exist without an assigned manager yet).</summary>
    public Guid? ManagerEmployeeId { get; private set; }

    /// <summary>Cross-module reference to a future Location entity (different roadmap item); column reserved, no FK yet.</summary>
    public Guid? LocationId { get; private set; }

    /// <summary>Default cost center inherited by employees assigned to this unit unless an employee override exists.</summary>
    public Guid? CostCenterId { get; private set; }

    /// <summary>Display order among siblings under the same parent.</summary>
    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<OrgUnit> Create(Guid tenantId, Guid typeId, Guid? parentId, string name, int sortOrder = 0, Guid? id = null)
    {
        if (typeId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitTypeIdRequired);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitNameRequired);
        }

        var unit = new OrgUnit(id ?? Guid.CreateVersion7(), tenantId, typeId, parentId, Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength), sortOrder);
        unit.Raise(new OrgUnitCreated(unit.Id, tenantId, typeId, parentId, unit.Name));
        return unit;
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(OrganizationDomainErrors.OrgUnitNameRequired);
        }

        Name = Guard.MaxLength(name.Trim(), OrganizationLimits.NameMaxLength);
        Raise(new OrgUnitRenamed(Id, TenantId, Name));
        return Result.Success();
    }

    public void SetParent(Guid? parentId) => ParentId = parentId;

    public void AssignManager(Guid? managerEmployeeId) => ManagerEmployeeId = managerEmployeeId;

    public void AssignLocation(Guid? locationId) => LocationId = locationId;

    public void AssignCostCenter(Guid? costCenterId) => CostCenterId = costCenterId;

    public void Reorder(int sortOrder) => SortOrder = sortOrder;

    public Result Deactivate()
    {
        if (!IsActive)
        {
            return Error.Rule(OrganizationDomainErrors.OrgUnitAlreadyInactive);
        }

        IsActive = false;
        Raise(new OrgUnitDeactivated(Id, TenantId));
        return Result.Success();
    }

    public void Activate() => IsActive = true;
}

public sealed record OrgUnitCreated(Guid OrgUnitId, Guid TenantId, Guid TypeId, Guid? ParentId, string Name) : DomainEvent;

public sealed record OrgUnitRenamed(Guid OrgUnitId, Guid TenantId, string Name) : DomainEvent;

public sealed record OrgUnitDeactivated(Guid OrgUnitId, Guid TenantId) : DomainEvent;
