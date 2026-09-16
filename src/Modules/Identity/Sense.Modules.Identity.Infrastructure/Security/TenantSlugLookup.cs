using Sense.Modules.Identity.Contracts;
using Sense.Modules.Identity.Domain;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Identity.Contracts.ITenantSlugLookup: <see cref="ITenantRepository.GetBySlugAsync"/> sarmalayıcısı
/// (ör. Faz 9 kariyer sayfası). Bulunamayan veya pasif (Suspended/Closed) kiracı için <c>null</c> döner.</summary>
public sealed class TenantSlugLookup(ITenantRepository tenantRepository) : ITenantSlugLookup
{
    public async Task<Guid?> FindTenantIdAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var tenant = await tenantRepository.GetBySlugAsync(slug.Trim().ToLowerInvariant(), cancellationToken).ConfigureAwait(false);
        return tenant is { IsWritable: true } ? tenant.Id : null;
    }
}
