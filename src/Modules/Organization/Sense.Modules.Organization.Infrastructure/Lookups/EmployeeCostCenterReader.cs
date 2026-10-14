using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;
using Sense.Shared.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Employee-owned assignment, then legacy Organization override, then nearest assigned ancestor.</summary>
public sealed class EmployeeCostCenterReader(
    IEmployeeCostCenterLookup directAssignments,
    OrganizationDbContext? context = null,
    IEmployeeLookup? employees = null) : IEmployeeCostCenterReader
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetEmployeeCostCenterMappingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken = default)
    {
        var direct = await directAssignments.GetAsync(tenantId, employeeIds, cancellationToken);
        if (context is null || employees is null || employeeIds.Count == 0) return direct;
        var unresolved = employeeIds.Distinct().Except(direct.Keys).ToArray();
        if (unresolved.Length == 0) return direct;

        var unitByEmployee = await employees.GetOrgUnitIdsAsync(tenantId, unresolved, cancellationToken);
        // Background callers supply the tenant explicitly; retain any soft-delete filters.
        var units = await context.OrgUnits.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).AsNoTracking()
            .Where(unit => unit.TenantId == tenantId)
            .Select(unit => new { unit.Id, unit.ParentId, unit.CostCenterId })
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        var overrides = await context.EmployeeCostCenterOverrides.IgnoreQueryFilters([ModuleDbContext.TenantFilter]).AsNoTracking()
            .Where(assignment => assignment.TenantId == tenantId && unresolved.Contains(assignment.EmployeeId))
            .ToDictionaryAsync(assignment => assignment.EmployeeId, assignment => assignment.CostCenterId, cancellationToken);

        var result = new Dictionary<Guid, Guid>(direct);
        foreach (var employeeId in unresolved)
        {
            if (overrides.TryGetValue(employeeId, out var overrideId))
            {
                result[employeeId] = overrideId;
                continue;
            }
            if (!unitByEmployee.TryGetValue(employeeId, out var unitId)) continue;
            var visited = new HashSet<Guid>();
            while (visited.Add(unitId) && units.TryGetValue(unitId, out var unit))
            {
                if (unit.CostCenterId is { } centerId)
                {
                    result[employeeId] = centerId;
                    break;
                }
                if (unit.ParentId is not { } parentId) break;
                unitId = parentId;
            }
        }
        return result;
    }
}
