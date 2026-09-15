using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Application.Tenants;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// <see cref="IAccessSettingsService"/> uygulaması: mevcut kiracının <see cref="Tenant.AccessSettings"/> alanından
/// tek anahtar okur (<see cref="AccessSettingsRegistry"/> ile). AuthorizationBehaviour ile aynı desende:
/// PermissionService kullanıcı izinlerini, bu servis kiracı özelliği açık mı sorusunu yanıtlar.
/// </summary>
public sealed class AccessSettingsService(IdentityDbContext db, ITenantContext tenant) : IAccessSettingsService
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
}
