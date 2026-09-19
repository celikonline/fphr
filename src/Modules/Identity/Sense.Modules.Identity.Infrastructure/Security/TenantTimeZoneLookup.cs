using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Identity.Contracts.ITenantTimeZoneLookup: <see cref="ITenantRepository.GetByIdAsync"/> sarmalayıcısı.
/// Yalnız gerçekten çözümlenebilen bir saat dilimi kimliği döner (bozuk değer null olur).</summary>
public sealed class TenantTimeZoneLookup(ITenantRepository tenantRepository) : ITenantTimeZoneLookup
{
    public async Task<string?> FindTimeZoneIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenantRepository.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null || string.IsNullOrWhiteSpace(tenant.TimeZone))
        {
            return null;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(tenant.TimeZone, out _) ? tenant.TimeZone : null;
    }
}
