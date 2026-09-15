using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Domain.Impersonation;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Context;
using Sense.Shared.Contracts.Security;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Platform personelinin kiracıda işlem yapabilmesinin tek yolu: `act` claim'li impersonation token'ı (sub = act = platform kullanıcısı)
/// ve bu kiracıda o kullanıcıya ait, başlatılmış (Active), iptal edilmemiş, süresi dolmamış bir grant. Her istekte veritabanından
/// okunur (önbelleksiz) — iptal anında etkili olur. JWT'deki platform_admin bayrağı tek başına hiçbir erişim vermez.
/// </summary>
public sealed class ImpersonationSessionValidator(IdentityDbContext db, ICurrentUser user, ITenantContext tenant, TimeProvider clock)
    : IImpersonationSessionValidator
{
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!user.IsAuthenticated || !user.IsPlatformAdmin || user.ImpersonatorUserId is not { } actor || user.UserId != actor || !tenant.IsResolved)
        {
            return false;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var tenantId = tenant.TenantId;
        return await db.ImpersonationGrants.AsNoTracking()
            .AnyAsync(
                g => g.TenantId == tenantId && g.PlatformUserId == actor && g.Status == ImpersonationGrantStatus.Active
                    && g.RevokedAt == null && g.ExpiresAt != null && g.ExpiresAt > now,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
