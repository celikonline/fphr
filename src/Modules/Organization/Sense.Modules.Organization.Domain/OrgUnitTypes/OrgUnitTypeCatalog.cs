using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.OrgUnitTypes;

/// <summary>
/// Seed catalog for system org-unit levels, applied once per tenant (at tenant creation / demo seed).
/// Active by default: Company &gt; Branch &gt; Department &gt; Title. Available but inactive until a
/// tenant opts in (Faz 0 madde 4 / 11.3.02-sirket-yapisi.md): Team, Project, ApprovalUnit. Names are
/// seeded directly in Turkish for now (no i18n resource-key indirection in this Faz 0 pass - the
/// design doc's NameKey/CustomName distinction is left as a follow-up if localized level names are
/// needed before a tenant renames them).
/// </summary>
public static class OrgUnitTypeCatalog
{
    public sealed record Seed(string Code, string Name, int SortOrder, bool IsActive, bool AllowsNesting);

    public static readonly IReadOnlyList<Seed> SystemTypes =
    [
        new(OrgUnitTypeCodes.Company, "Şirket", 1, true, false),
        new(OrgUnitTypeCodes.Branch, "Şube", 2, true, false),
        new(OrgUnitTypeCodes.Department, "Departman", 3, true, true),
        new(OrgUnitTypeCodes.Title, "Ünvan", 4, true, false),
        new(OrgUnitTypeCodes.Team, "Takım", 5, false, true),
        new(OrgUnitTypeCodes.Project, "Proje", 6, false, true),
        new(OrgUnitTypeCodes.ApprovalUnit, "Onay süreci birimi", 7, false, false),
    ];

    /// <summary>Builds Create results for every system type for a new tenant; callers should add each successful Value to the DbContext.</summary>
    public static IReadOnlyList<Result<OrgUnitType>> CreateAll(Guid tenantId) =>
        SystemTypes
            .Select(s => OrgUnitType.Create(tenantId, s.Code, s.Name, s.SortOrder, isSystem: true, isActive: s.IsActive, allowsNesting: s.AllowsNesting))
            .ToList();
}
