using Sense.Modules.Identity.Application.Roles;
using Sense.Modules.Identity.Domain;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Application.Tenants;

/// <summary>Yeni kiracıya sistem rollerini kataloğa göre izinleriyle seed eder (kiracı oluşturma, platform bootstrap, testler).</summary>
public static class TenantRoleSeeder
{
    /// <param name="isPlatformTenant">true ise müşteri rollerine ek olarak yalnız platform kiracısına özel roller (SuperAdmin) de oluşturulur.</param>
    public static IReadOnlyDictionary<string, Role> SeedSystemRoles(
        Guid tenantId, IPermissionCatalog catalog, IRoleRepository roles, bool isPlatformTenant = false)
    {
        var result = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in SystemRoleCodes.ForTenant(isPlatformTenant))
        {
            var role = Role.CreateSystem(tenantId, code);
            role.ReplacePermissionsUnchecked(SystemRolePermissionResolver.Resolve(role, catalog.All));
            roles.Add(role);
            result[code] = role;
        }

        return result;
    }
}
