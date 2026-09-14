using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Caching;
using Sense.Shared.Infrastructure.Messaging.Behaviours;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Kullanıcının etkin izin seti: roller ∪ (Allow override) − (Deny override). HybridCache'te kiracı/kullanıcı anahtarıyla.</summary>
public sealed class PermissionService(IdentityDbContext db, HybridCache cache, ITenantContext tenant, IOptions<CachingOptions> caching)
    : IPermissionService, IPermissionCacheInvalidator
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    public async Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default) =>
        (await GetPermissionsAsync(userId, cancellationToken).ConfigureAwait(false)).Contains(permission);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(userId, cancellationToken).ConfigureAwait(false);
        return snapshot is null ? Empty : new HashSet<string>(snapshot.Permissions.Keys, StringComparer.Ordinal);
    }

    public async Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(userId, cancellationToken).ConfigureAwait(false);
        return snapshot is not null && snapshot.Permissions.TryGetValue(permission, out var scope) ? scope : DataScope.Self;
    }

    public Task InvalidateUserAsync(Guid userId, CancellationToken ct) => cache.RemoveAsync(Key(userId), ct).AsTask();

    public Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct) => cache.RemoveByTagAsync(TenantTag(tenantId), ct).AsTask();

    private async Task<PermissionSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!tenant.IsResolved)
        {
            return null;
        }

        return await cache.GetOrCreateInContextAsync(
            Key(userId),
            async ct => await LoadAsync(userId, ct).ConfigureAwait(false),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(caching.Value.PermissionExpirationMinutes) },
            [TenantTag(tenant.TenantId)],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PermissionSnapshot?> LoadAsync(Guid userId, CancellationToken ct)
    {
        // Kiracı bağlamı yoksa kiracı filtresi boş kiracıyla çalışır ve boş izin seti önbelleğe yazılır; sessizce devam etmek yerine hata ver.
        if (!tenant.IsResolved)
        {
            throw new InvalidOperationException(PermissionServiceMessages.TenantContextLost);
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToListAsync(ct).ConfigureAwait(false);

        // En geniş kapsam kazanır.
        var effective = new Dictionary<string, DataScope>(StringComparer.Ordinal);
        foreach (var p in roles.SelectMany(r => r.Permissions))
        {
            var scope = (DataScope)p.Scope;
            effective[p.Permission] = effective.TryGetValue(p.Permission, out var existing) ? DataScopes.Widest(existing, scope) : scope;
        }

        foreach (var o in user.PermissionOverrides)
        {
            if (o.Allow)
            {
                effective.TryAdd(o.Permission, DataScope.Company);
            }
            else
            {
                effective.Remove(o.Permission);
            }
        }

        return new PermissionSnapshot(effective);
    }

    private string Key(Guid userId) => CacheKeys.Join(CacheKeyPrefixes.Permission, tenant.TenantId.ToString("N"), userId.ToString("N"));

    private static string TenantTag(Guid tenantId) => CacheKeys.Join(CacheKeyPrefixes.PermissionTenant, tenantId.ToString("N"));

    public sealed record PermissionSnapshot(Dictionary<string, DataScope> Permissions);
}

internal static class PermissionServiceMessages
{
    public const string TenantContextLost = "Permission snapshot requested without a resolved tenant context.";
}

public static class CacheKeyPrefixes
{
    public const string Permission = "perm";
    public const string PermissionTenant = "perm-tenant";
}

/// <summary>
/// Veri kapsamı çözümü. Faz 0: Self/Company; Team/Department/Location için Employee modülünün hiyerarşi kontratı (IEmployeeScopeLookup)
/// kayıtlıysa kullanılır, değilse Self'e düşer (güvenli varsayılan).
/// </summary>
public sealed class DataScopeService(IPermissionService permissions, ICurrentUser user, IEmployeeScopeLookup? scopeLookup = null) : IDataScopeService
{
    public async Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        if (user.IsPlatformAdmin)
        {
            return null;
        }

        if (user.UserId is null)
        {
            return new HashSet<Guid>();
        }

        var scope = await permissions.GetScopeAsync(user.UserId.Value, permission, cancellationToken).ConfigureAwait(false);
        if (scope == DataScope.Company)
        {
            return null;
        }

        var self = user.EmployeeId is { } eid ? new HashSet<Guid> { eid } : new HashSet<Guid>();
        if (scope == DataScope.Self || scopeLookup is null || user.EmployeeId is null)
        {
            return self;
        }

        var extra = await scopeLookup.GetEmployeeIdsInScopeAsync(user.EmployeeId.Value, scope, cancellationToken).ConfigureAwait(false);
        self.UnionWith(extra);
        return self;
    }

    public async Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var visible = await GetVisibleEmployeeIdsAsync(permission, cancellationToken).ConfigureAwait(false);
        return visible is null || visible.Contains(employeeId);
    }
}

/// <summary>Tüm modüllerin izin katalogları (IModule.Permissions) birleşimi.</summary>
public sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly Lazy<IReadOnlyList<Permission>> _all;
    private readonly Lazy<HashSet<string>> _keys;

    public PermissionCatalog(IReadOnlyList<IModule> modules)
    {
        _all = new Lazy<IReadOnlyList<Permission>>(() =>
            modules.SelectMany(m => m.Permissions)
                .GroupBy(p => p.Key, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToList());
        _keys = new Lazy<HashSet<string>>(() => _all.Value.Select(p => p.Key).ToHashSet(StringComparer.Ordinal));
    }

    public IReadOnlyList<Permission> All => _all.Value;

    public bool Exists(string permission) => _keys.Value.Contains(permission);
}
