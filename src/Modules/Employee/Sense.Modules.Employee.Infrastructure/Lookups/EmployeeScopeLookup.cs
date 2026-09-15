using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>
/// Shared.Contracts IEmployeeScopeLookup for Identity's DataScopeService: Team = direct and indirect reports (ManagerEmployeeId chain),
/// Department = employees of the caller's org unit and its active descendants. Location/Custom are not modeled on employees yet (empty).
/// </summary>
public sealed class EmployeeScopeLookup(EmployeeDbContext context, ITenantContext tenant, IOrgUnitLookup orgUnits) : IEmployeeScopeLookup
{
    private static readonly IReadOnlySet<Guid> Empty = new HashSet<Guid>();

    public async Task<IReadOnlySet<Guid>> GetEmployeeIdsInScopeAsync(Guid employeeId, DataScope scope, CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case DataScope.Team:
            {
                var managerMap = await context.Employees.AsNoTracking()
                    .Select(e => new { e.Id, e.ManagerEmployeeId })
                    .ToDictionaryAsync(e => e.Id, e => e.ManagerEmployeeId, cancellationToken);
                return ManagerHierarchy.ReportsOf(employeeId, managerMap);
            }

            case DataScope.Department:
            {
                var orgUnitId = await context.Employees.AsNoTracking()
                    .Where(e => e.Id == employeeId)
                    .Select(e => e.OrgUnitId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (orgUnitId is null)
                {
                    return Empty;
                }

                var unitIds = (await orgUnits.GetSelfAndDescendantIdsAsync(tenant.TenantId, [orgUnitId.Value], cancellationToken)).ToList();
                var ids = await context.Employees.AsNoTracking()
                    .Where(e => e.OrgUnitId != null && unitIds.Contains(e.OrgUnitId.Value))
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);
                return ids.ToHashSet();
            }

            default:
                return Empty;
        }
    }
}
