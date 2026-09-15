using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// <see cref="IAccessSettingsService"/> uygulaması: mevcut kiracının <see cref="Tenant.AccessSettings"/> alanından
/// tek anahtar okur (<see cref="AccessSettingsRegistry"/> ile). Erişim ayarları yalnız çalışan tipini kısıtlar:
/// <see cref="AccessSettingPermissions.Bypass"/> sahibi (Owner/Admin/HR) veya aktif impersonation oturumu için etkin değer her zaman true'dur.
/// </summary>
public sealed class AccessSettingsService(
    IdentityDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    IPermissionService permissions,
    IImpersonationSessionValidator? impersonationSessions = null) : IAccessSettingsService
{
    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!tenant.IsResolved || !AccessSettingsRegistry.Getters.TryGetValue(key, out var getter))
        {
            // Bilinmeyen anahtar veya kiracı bağlamı yok: güvenli varsayılan (görünürlük anahtarı, yetki değil).
            return true;
        }

        var current = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenant.TenantId, cancellationToken)
            .ConfigureAwait(false);

        return getter(current?.AccessSettings ?? TenantAccessSettings.Default);
    }

    public async Task<bool> IsEnabledForCurrentUserAsync(string key, CancellationToken cancellationToken = default)
    {
        if (user.ClaimsPlatformAccess())
        {
            if (impersonationSessions is not null && await impersonationSessions.IsActiveAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }
        else if (user.UserId is { } userId && tenant.IsResolved
            && await permissions.HasAsync(userId, AccessSettingPermissions.Bypass, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return await IsEnabledAsync(key, cancellationToken).ConfigureAwait(false);
    }
}
