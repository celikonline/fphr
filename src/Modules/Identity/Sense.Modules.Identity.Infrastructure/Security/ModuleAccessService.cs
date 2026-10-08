using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using Sense.Modules.Identity.Application;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Configuration;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Modules;
using Sense.Shared.Infrastructure.Caching;
using Sense.Shared.Infrastructure.Messaging.Behaviours;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// <see cref="IModuleAccessService"/> uygulaması (senseik-057): mevcut kiracının <c>Tenant.ModuleSettings</c> alanını
/// HybridCache'te kiracı anahtarıyla tutar (her HTTP isteğinde ModuleAccessFilter tarafından okunur).
/// Modüller sayfası "Kaydet" eylemi <see cref="IModuleSettingsCacheInvalidator"/> ile girdiyi siler.
/// Kapatma/açma herkes için aynıdır: izin veya impersonation bypass'ı yoktur.
/// </summary>
public sealed class ModuleAccessService(
    IdentityDbContext db,
    HybridCache cache,
    ITenantContext tenant,
    IOptions<CachingOptions> caching) : IModuleAccessService, IModuleSettingsCacheInvalidator
{
    public async Task<IReadOnlyCollection<string>> GetDisabledModulesAsync(CancellationToken cancellationToken = default)
    {
        if (!tenant.IsResolved)
        {
            return [];
        }

        var tenantId = tenant.TenantId;
        // Sales-managed entitlements are checked directly; a revoked or upgraded license must not wait for cache expiry.
        var salesTenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken).ConfigureAwait(false);
        if (salesTenant is not null && Sense.Modules.Identity.Domain.Tenants.CommercialLicense.Read(salesTenant.SettingsJson) is not null)
            return Sense.Modules.Identity.Application.Tenants.CommercialLicenseRules.DisabledModules(salesTenant);
        return await cache.GetOrCreateInContextAsync(
            Key(tenantId),
            async ct => await LoadAsync(tenantId, ct).ConfigureAwait(false),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(caching.Value.DefaultExpirationMinutes) },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsEnabledAsync(string moduleKey, CancellationToken cancellationToken = default)
    {
        if (!ProductModules.IsToggleable(moduleKey))
        {
            return true;
        }

        var disabled = await GetDisabledModulesAsync(cancellationToken).ConfigureAwait(false);
        return !disabled.Contains(moduleKey, StringComparer.Ordinal);
    }

    public Task InvalidateTenantAsync(Guid tenantId, CancellationToken ct) => cache.RemoveAsync(Key(tenantId), ct).AsTask();

    private async Task<string[]> LoadAsync(Guid tenantId, CancellationToken ct)
    {
        var current = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            .ConfigureAwait(false);

        return current is null ? [] : Sense.Modules.Identity.Application.Tenants.CommercialLicenseRules.DisabledModules(current).ToArray();
    }

    private static string Key(Guid tenantId) => CacheKeys.Join(ModuleCacheKeyPrefixes.DisabledModules, tenantId.ToString("N"));
}

public static class ModuleCacheKeyPrefixes
{
    public const string DisabledModules = "modules-disabled-v1";
}
