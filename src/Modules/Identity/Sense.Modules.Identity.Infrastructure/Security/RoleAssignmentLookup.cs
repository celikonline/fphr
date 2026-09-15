using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Infrastructure.Persistence;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Identity.Contracts.IRoleAssignmentLookup: rol kodu üzerinden çalışan eşlemesi (User.EmployeeId üzerinden).</summary>
public sealed class RoleAssignmentLookup(IdentityDbContext db) : IRoleAssignmentLookup
{
    public async Task<IReadOnlyList<Guid>> ListEmployeeIdsInRoleAsync(Guid tenantId, string roleCode, CancellationToken ct = default)
    {
        var roleIds = await RoleIdsAsync(tenantId, roleCode, ct).ConfigureAwait(false);
        if (roleIds.Count == 0)
        {
            return [];
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.EmployeeId != null && u.Roles.Any(r => roleIds.Contains(r.RoleId)))
            .Select(u => u.EmployeeId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> EmployeeHasRoleAsync(Guid tenantId, Guid employeeId, string roleCode, CancellationToken ct = default)
    {
        var roleIds = await RoleIdsAsync(tenantId, roleCode, ct).ConfigureAwait(false);
        if (roleIds.Count == 0)
        {
            return false;
        }

        return await db.Users.AsNoTracking()
            .AnyAsync(u => u.TenantId == tenantId && u.EmployeeId == employeeId && u.Roles.Any(r => roleIds.Contains(r.RoleId)), ct)
            .ConfigureAwait(false);
    }

    private async Task<List<Guid>> RoleIdsAsync(Guid tenantId, string roleCode, CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Code == roleCode)
            .Select(r => r.Id)
            .ToListAsync(ct).ConfigureAwait(false);
}
