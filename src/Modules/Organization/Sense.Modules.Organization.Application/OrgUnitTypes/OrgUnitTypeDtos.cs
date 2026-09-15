namespace Sense.Modules.Organization.Application.OrgUnitTypes;

/// <summary>A hierarchy level (Company/Branch/Department/Title/...) as exposed to clients.</summary>
public sealed record OrgUnitTypeDto(
    Guid Id,
    string Code,
    string Name,
    int SortOrder,
    bool IsActive,
    bool IsSystem,
    bool AllowsNesting);
