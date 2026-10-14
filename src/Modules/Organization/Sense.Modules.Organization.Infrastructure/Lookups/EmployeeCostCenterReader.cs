using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Resolves employee override first, then the nearest cost center in the employee's org-unit ancestry.</summary>
public sealed class EmployeeCostCenterReader(
    OrganizationDbContext context,
    IEmployeeLookup employees) : IEmployeeCostCenterReader
{
    public async Task<IReadOnlyDictionary<Guid, Guid>> GetEmployeeCostCenterMappingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken = default)
    {
        if (employeeIds.Count == 0) return new Dictionary<Guid, Guid>();

        var requested = employeeIds.Distinct().ToArray();
        var unitByEmployee = await employees.GetOrgUnitIdsAsync(tenantId, requested, cancellationToken);
        var units = await context.OrgUnits.AsNoTracking()
            .Select(u => new { u.Id, u.ParentId, u.CostCenterId })
            .ToDictionaryAsync(u => u.Id, cancellationToken);
        var overrides = await context.EmployeeCostCenterOverrides.AsNoTracking()
            .Where(x => requested.Contains(x.EmployeeId))
            .ToDictionaryAsync(x => x.EmployeeId, x => x.CostCenterId, cancellationToken);

        var result = new Dictionary<Guid, Guid>();
        foreach (var employeeId in requested)
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
