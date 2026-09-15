using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Sense.Modules.Employee.Contracts;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Application.UnitAuthorizations;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Modules.Organization.Contracts;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Contracts.Security;
using Sense.Shared.Infrastructure.Caching;
using Sense.Shared.Infrastructure.Messaging.Behaviours;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Kullanıcının etkin izin seti: roller ∪ birim yetkileri (UnitManagerGrants) ∪ (Allow override) − (Deny override).
/// Birim yetkileri kapsamı rol kapsamından ayrı tutulur (izin → yetkili kök birimler); alt birimler sorgu anında çözülür.
/// HybridCache'te kiracı/kullanıcı anahtarıyla; rol/override/birim yetkisi değişiminde geçersizlenir.
/// </summary>
public sealed class PermissionService(
    IdentityDbContext db,
    HybridCache cache,
    ITenantContext tenant,
    IOptions<CachingOptions> caching,
    IOrgUnitLookup? orgUnits = null) : IPermissionService, IPermissionCacheInvalidator
{
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>(StringComparer.Ordinal);

    public async Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(userId, cancellationToken).ConfigureAwait(false);
        return snapshot is not null && snapshot.Has(permission);
    }

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(userId, cancellationToken).ConfigureAwait(false);
        return snapshot is null
            ? Empty
            : new HashSet<string>(snapshot.Permissions.Keys.Concat(snapshot.UnitGrants.Keys), StringComparer.Ordinal);
    }

    /// <summary>Rol kapsamı ile birim yetkisi (Department) birleşimi: en geniş kazanır; izin yoksa Self.</summary>
    public async Task<DataScope> GetScopeAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var detail = await GetScopeDetailAsync(userId, permission, cancellationToken).ConfigureAwait(false);
        return (detail.RoleScope, detail.UnitIds.Count > 0) switch
        {
            ({ } role, true) => DataScopes.Widest(role, DataScope.Department),
            ({ } role, false) => role,
            (null, true) => DataScope.Department,
            _ => DataScope.Self,
        };
    }

    /// <summary>Rol/override kaynaklı kapsam (yoksa null) ve bu izni birim yetkisiyle veren kök birimler.</summary>
    public async Task<PermissionScopeDetail> GetScopeDetailAsync(Guid userId, string permission, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(userId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return PermissionScopeDetail.None;
        }

        DataScope? roleScope = snapshot.Permissions.TryGetValue(permission, out var scope) ? scope : null;
        IReadOnlyList<Guid> units = snapshot.UnitGrants.TryGetValue(permission, out var ids) ? ids : [];
        return new PermissionScopeDetail(roleScope, units);
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

        var unitGrants = await LoadUnitGrantsAsync(userId, ct).ConfigureAwait(false);

        foreach (var o in user.PermissionOverrides)
        {
            if (o.Allow)
            {
                effective.TryAdd(o.Permission, DataScope.Company);
            }
            else
            {
                effective.Remove(o.Permission);
                unitGrants.Remove(o.Permission);
            }
        }

        return new PermissionSnapshot(effective, unitGrants);
    }

    private async Task<Dictionary<string, List<Guid>>> LoadUnitGrantsAsync(Guid userId, CancellationToken ct)
    {
        var result = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var grants = await db.UnitAuthorizations.AsNoTracking().Where(a => a.UserId == userId && a.IsActive).ToListAsync(ct).ConfigureAwait(false);
        if (grants.Count == 0)
        {
            return result;
        }

        // Pasif (veya silinmiş) birimlere verilmiş yetkiler yok sayılır; Organization modülü yüklü değilse hiçbiri geçerli sayılmaz.
        var unitIds = grants.Select(g => g.OrgUnitId).Distinct().ToList();
        var activeUnits = orgUnits is null
            ? new HashSet<Guid>()
            : (await orgUnits.GetSelfAndDescendantIdsAsync(tenant.TenantId, unitIds, ct).ConfigureAwait(false)).ToHashSet();

        foreach (var grant in grants.Where(g => activeUnits.Contains(g.OrgUnitId)))
        {
            foreach (var permission in UnitManagerGrants.For(grant.CanViewSalaryAndPayments, grant.CanViewUnitReports))
            {
                if (!result.TryGetValue(permission, out var list))
                {
                    result[permission] = list = [];
                }

                if (!list.Contains(grant.OrgUnitId))
                {
                    list.Add(grant.OrgUnitId);
                }
            }
        }

        return result;
    }

    private string Key(Guid userId) => CacheKeys.Join(CacheKeyPrefixes.Permission, tenant.TenantId.ToString("N"), userId.ToString("N"));

    private static string TenantTag(Guid tenantId) => CacheKeys.Join(CacheKeyPrefixes.PermissionTenant, tenantId.ToString("N"));

    /// <summary>UnitGrants: izin → bu izni birim yetkisiyle veren (aktif) kök birimler.</summary>
    public sealed record PermissionSnapshot(Dictionary<string, DataScope> Permissions, Dictionary<string, List<Guid>> UnitGrants)
    {
        public bool Has(string permission) => Permissions.ContainsKey(permission) || UnitGrants.ContainsKey(permission);
    }
}

/// <summary>RoleScope: rol/override kaynaklı kapsam (izin rolden gelmiyorsa null); UnitIds: birim yetkisi kök birimleri.</summary>
public sealed record PermissionScopeDetail(DataScope? RoleScope, IReadOnlyList<Guid> UnitIds)
{
    public static readonly PermissionScopeDetail None = new(null, []);
}

internal static class PermissionServiceMessages
{
    public const string TenantContextLost = "Permission snapshot requested without a resolved tenant context.";
}

public static class CacheKeyPrefixes
{
    // v2: snapshot gained unit grants; a new prefix keeps old L2 (Redis) entries from being deserialized into the new shape.
    public const string Permission = "perm-v2";
    public const string PermissionTenant = "perm-tenant";
}

/// <summary>
/// Veri kapsamı çözümü: rol kapsamı (Self/Team/Department/Location/Company; hiyerarşi IEmployeeScopeLookup ile) ∪ birim yetkileri
/// (yetkili birim + aktif alt birimlerindeki aktif çalışanlar). Platform kimliği yalnız aktif impersonation oturumuyla sınırsızdır;
/// gerekli kontratlar kayıtlı değilse güvenli tarafta (dar kapsam) kalınır.
/// </summary>
public sealed class DataScopeService(
    PermissionService permissions,
    ICurrentUser user,
    ITenantContext tenant,
    IServiceProvider services,
    IImpersonationSessionValidator? impersonationSessions = null,
    IEmployeeScopeLookup? scopeLookup = null,
    IOrgUnitLookup? orgUnits = null,
    IEmployeeLookup? employeeLookup = null) : IDataScopeService
{
    private static readonly IReadOnlySet<Guid> Nothing = new HashSet<Guid>();

    public async Task<IReadOnlySet<Guid>?> GetVisibleEmployeeIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        var platform = await ResolvePlatformAsync(cancellationToken).ConfigureAwait(false);
        if (platform is not null)
        {
            return platform.Value ? null : Nothing;
        }

        if (user.UserId is not { } userId)
        {
            return new HashSet<Guid>();
        }

        var detail = await permissions.GetScopeDetailAsync(userId, permission, cancellationToken).ConfigureAwait(false);
        if (detail.RoleScope == DataScope.Company)
        {
            return null;
        }

        var visible = user.EmployeeId is { } eid ? new HashSet<Guid> { eid } : new HashSet<Guid>();
        if (detail.RoleScope is DataScope.Team or DataScope.Department or DataScope.Location && scopeLookup is not null && user.EmployeeId is { } self)
        {
            visible.UnionWith(await scopeLookup.GetEmployeeIdsInScopeAsync(self, detail.RoleScope.Value, cancellationToken).ConfigureAwait(false));
        }

        // Resolved lazily: Employee's directory itself depends on IDataScopeService (selection resolution).
        if (detail.UnitIds.Count > 0 && orgUnits is not null && tenant.IsResolved && services.GetService(typeof(IEmployeeDirectory)) is IEmployeeDirectory employeeDirectory)
        {
            var units = await orgUnits.GetSelfAndDescendantIdsAsync(tenant.TenantId, detail.UnitIds.ToList(), cancellationToken).ConfigureAwait(false);
            visible.UnionWith(await employeeDirectory.ListActiveEmployeeIdsInOrgUnitsAsync(tenant.TenantId, units, cancellationToken).ConfigureAwait(false));
        }

        return visible;
    }

    public async Task<bool> CanAccessEmployeeAsync(string permission, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var visible = await GetVisibleEmployeeIdsAsync(permission, cancellationToken).ConfigureAwait(false);
        return visible is null || visible.Contains(employeeId);
    }

    public async Task<IReadOnlySet<Guid>?> GetAccessibleOrgUnitIdsAsync(string permission, CancellationToken cancellationToken = default)
    {
        var platform = await ResolvePlatformAsync(cancellationToken).ConfigureAwait(false);
        if (platform is not null)
        {
            return platform.Value ? null : Nothing;
        }

        if (user.UserId is not { } userId || !tenant.IsResolved)
        {
            return new HashSet<Guid>();
        }

        var detail = await permissions.GetScopeDetailAsync(userId, permission, cancellationToken).ConfigureAwait(false);
        if (detail.RoleScope == DataScope.Company)
        {
            return null;
        }

        var roots = new List<Guid>(detail.UnitIds);
        if (detail.RoleScope == DataScope.Department && employeeLookup is not null && user.EmployeeId is { } self
            && await employeeLookup.GetOrgUnitIdAsync(tenant.TenantId, self, cancellationToken).ConfigureAwait(false) is { } ownUnit)
        {
            roots.Add(ownUnit);
        }

        if (roots.Count == 0 || orgUnits is null)
        {
            return new HashSet<Guid>();
        }

        return (await orgUnits.GetSelfAndDescendantIdsAsync(tenant.TenantId, roots.Distinct().ToList(), cancellationToken).ConfigureAwait(false)).ToHashSet();
    }

    /// <summary>null: platform kimliği değil (normal kural); true: sınırsız (sistem bağlamı veya aktif impersonation); false: erişim yok.</summary>
    private async Task<bool?> ResolvePlatformAsync(CancellationToken ct)
    {
        if (user.IsSystemContext())
        {
            return true;
        }

        if (!user.ClaimsPlatformAccess())
        {
            return null;
        }

        return impersonationSessions is not null && await impersonationSessions.IsActiveAsync(ct).ConfigureAwait(false);
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
