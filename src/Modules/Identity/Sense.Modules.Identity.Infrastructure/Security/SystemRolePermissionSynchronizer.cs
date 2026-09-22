using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Domain.Roles;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Context;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Sistem rollerinin izinlerini güncel <see cref="Application.Roles.SystemRoleDefinitions"/> + birleşik izin kataloğuna göre yeniden
/// uygular (idempotent). Yeni modül/izin anahtarları yalnız kiracı kaydında yazıldığı için mevcut kiracılara bu yolla ulaşır.
/// Yalnız IsSystem rollerine dokunur (eksik sistem rolleri oluşturulur); özel roller ve kullanıcı override'ları değişmez.
/// </summary>
public sealed partial class SystemRolePermissionSynchronizer(
    IdentityDbContext db,
    IPermissionCatalog catalog,
    ITenantContextSetter tenantSetter,
    IPermissionCacheInvalidator cache,
    ILogger<SystemRolePermissionSynchronizer> logger)
{
    /// <returns>Değişen (veya oluşturulan) sistem rolü sayısı.</returns>
    public async Task<int> SyncAllTenantsAsync(CancellationToken ct)
    {
        var tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            total += await SyncTenantAsync(tenantId, ct).ConfigureAwait(false);
        }

        return total;
    }

    public async Task<int> SyncTenantAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = tenantSetter.BeginScope(tenantId);

        var slug = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Slug).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var systemRoles = await db.Roles.Where(r => r.IsSystem).ToListAsync(ct).ConfigureAwait(false);
        var changed = 0;
        var current = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);

        // SuperAdmin gibi platforma özel roller yalnız platform kiracısında oluşturulur/senkronize edilir.
        foreach (var code in SystemRoleCodes.ForTenant(PlatformTenant.IsPlatform(slug)))
        {
            var role = systemRoles.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                role = Role.CreateSystem(tenantId, code);
                db.Roles.Add(role);
                role.ReplacePermissionsUnchecked(Desired(role));
                current[code] = role;
                changed++;
                continue;
            }

            current[code] = role;
            if (role.SyncPermissionsUnchecked(Desired(role)))
            {
                changed++;
            }
        }

        changed += await MigrateToUserGroupsAsync(systemRoles, current, ct).ConfigureAwait(false);

        if (changed > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await cache.InvalidateTenantAsync(tenantId, ct).ConfigureAwait(false);
            LogTenantSynced(logger, tenantId, changed);
        }

        return changed;
    }

    /// <summary>
    /// Dört gruba geçiş (idempotent): kaldırılan sistem rollerinin kullanıcıları <see cref="SystemRoleCodes.RetiredSuccessors"/>
    /// grubuna taşınır ve eski roller silinir; İK Yöneticisi olmayan hesap sahiplerine (Owner) İK Yöneticisi eklenir.
    /// </summary>
    private async Task<int> MigrateToUserGroupsAsync(List<Role> systemRoles, Dictionary<string, Role> current, CancellationToken ct)
    {
        var retired = systemRoles.Where(r => SystemRoleCodes.RetiredSuccessors.ContainsKey(r.Code)).ToList();
        var successorByRetiredId = retired
            .Where(r => current.ContainsKey(SystemRoleCodes.RetiredSuccessors[r.Code]))
            .ToDictionary(r => r.Id, r => current[SystemRoleCodes.RetiredSuccessors[r.Code]].Id);
        var retiredIds = retired.Select(r => r.Id).ToList();
        var ownerId = current.GetValueOrDefault(SystemRoleCodes.Owner)?.Id;
        var hrId = current.GetValueOrDefault(SystemRoleCodes.HR)?.Id;

        var users = await db.Users
            .Where(u => u.Roles.Any(r => retiredIds.Contains(r.RoleId)) || (ownerId != null && u.Roles.Any(r => r.RoleId == ownerId)))
            .ToListAsync(ct).ConfigureAwait(false);

        var changed = 0;
        foreach (var user in users)
        {
            var roleIds = user.Roles.Select(r => r.RoleId).ToList();
            var target = roleIds
                .Select(id => successorByRetiredId.TryGetValue(id, out var successor) ? successor : id)
                .Where(id => !retiredIds.Contains(id))
                .ToHashSet();
            if (ownerId is { } owner && hrId is { } hr && target.Contains(owner))
            {
                target.Add(hr);
            }

            if (!target.SetEquals(roleIds))
            {
                user.SetRoles(target);
                changed++;
            }
        }

        if (retired.Count > 0)
        {
            db.Roles.RemoveRange(retired);
            LogRetiredRolesRemoved(logger, retired.Count, changed);
            changed += retired.Count;
        }

        return changed;
    }

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information, Message = "Retired system roles removed: {RoleCount} role(s), {UserCount} user(s) moved to the four user groups")]
    private static partial void LogRetiredRolesRemoved(ILogger logger, int roleCount, int userCount);

    private List<RolePermission> Desired(Role role) => Application.Roles.SystemRolePermissionResolver.Resolve(role, catalog.All);

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "System role permissions synchronized for tenant {TenantId}: {ChangedRoles} role(s) changed")]
    private static partial void LogTenantSynced(ILogger logger, Guid tenantId, int changedRoles);
}

/// <summary>API başlangıcında (seed'den sonra) sistem rolü izin senkronizasyonunu bir kez çalıştırır; hata başlatmayı durdurmaz.</summary>
public sealed partial class SystemRolePermissionSyncHostedService(IServiceScopeFactory scopes, ILogger<SystemRolePermissionSyncHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var system = CurrentUserAccessor.UseSystem();
            await using var scope = scopes.CreateAsyncScope();
            var synchronizer = scope.ServiceProvider.GetRequiredService<SystemRolePermissionSynchronizer>();
            await synchronizer.SyncAllTenantsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Veritabanı henüz migrate edilmemiş olabilir (Migrator ayrı çalışır); bir sonraki başlatmada tekrar denenir.
            LogSyncFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "System role permission synchronization failed; it will be retried on next start")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception);
}
