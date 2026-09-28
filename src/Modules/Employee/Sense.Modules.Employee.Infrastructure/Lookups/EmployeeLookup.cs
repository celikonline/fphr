using Microsoft.EntityFrameworkCore;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Employee.Domain.Employees;
using Sense.Modules.Employee.Infrastructure.Persistence;

namespace Sense.Modules.Employee.Infrastructure.Lookups;

/// <summary>Implements Employee.Contracts.IEmployeeLookup so other modules (e.g. Organization) can validate an employee reference.</summary>
public sealed class EmployeeLookup(EmployeeDbContext context) : IEmployeeLookup
{
    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AnyAsync(
            e => e.TenantId == tenantId && e.Id == employeeId && e.Status != EmployeeStatus.Terminated,
            cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveByOrgUnitAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OrgUnitId != null && e.Status != EmployeeStatus.Terminated)
            .GroupBy(e => e.OrgUnitId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

    public async Task<bool> AnyActiveInOrgUnitsAsync(Guid tenantId, IReadOnlyCollection<Guid> orgUnitIds, CancellationToken cancellationToken = default)
    {
        if (orgUnitIds.Count == 0)
        {
            return false;
        }

        var units = orgUnitIds.ToList();
        return await context.Employees.AnyAsync(
            e => e.TenantId == tenantId && e.OrgUnitId != null && units.Contains(e.OrgUnitId.Value) && e.Status != EmployeeStatus.Terminated,
            cancellationToken);
    }

    public async Task<Guid?> GetOrgUnitIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.OrgUnitId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> GetManagerEmployeeIdAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.ManagerEmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> FindActiveByNationalIdAsync(Guid tenantId, string nationalId, CancellationToken cancellationToken = default)
    {
        var matches = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.NationalId == nationalId && e.Status != EmployeeStatus.Terminated)
            .Select(e => e.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<Guid?> FindActiveBySicilNumarasiAsync(Guid tenantId, string sicilNumarasi, CancellationToken cancellationToken = default)
    {
        var matches = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.SicilNumarasi == sicilNumarasi && e.Status != EmployeeStatus.Terminated)
            .Select(e => e.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<string?> GetSgkOccupationCodeAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => e.SgkOccupationCode)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<EmployeeGender?> GetGenderAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var row = await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Id == employeeId)
            .Select(e => new { e.Gender })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : Enum.Parse<EmployeeGender>(row.Gender.ToString());
    }

    public async Task<IReadOnlyList<Guid>> GetDirectReportIdsAsync(Guid tenantId, Guid managerEmployeeId, CancellationToken cancellationToken = default) =>
        await context.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.ManagerEmployeeId == managerEmployeeId && e.Status != EmployeeStatus.Terminated)
            .OrderBy(e => e.DisplayName)
            .ThenBy(e => e.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
}
