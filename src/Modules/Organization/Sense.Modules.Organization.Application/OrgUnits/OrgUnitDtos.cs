namespace Sense.Modules.Organization.Application.OrgUnits;

/// <summary>Flat org unit shape (one row per hierarchy node).</summary>
public sealed record OrgUnitDto(
    Guid Id,
    Guid TypeId,
    Guid? ParentId,
    string Name,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    int SortOrder,
    bool IsActive,
    Guid? CostCenterId = null);

/// <summary>Nested tree node for GET /organization/units/tree - built in-memory from a flat list (see OrgUnitTreeBuilder).</summary>
public sealed record OrgUnitTreeNodeDto(
    Guid Id,
    Guid TypeId,
    string Name,
    Guid? ManagerEmployeeId,
    Guid? LocationId,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<OrgUnitTreeNodeDto> Children,
    Guid? CostCenterId = null);
