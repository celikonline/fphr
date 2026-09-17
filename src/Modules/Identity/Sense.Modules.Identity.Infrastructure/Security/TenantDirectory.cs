using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.BackgroundJobs;

namespace Sense.Modules.Identity.Infrastructure.Security;

/// <summary>Sense.Shared.Contracts.BackgroundJobs.IActiveTenantDirectory: yazılabilir (Trial/Active) kiracıların
/// kimlik listesi (ör. G-1-13 <c>BackgroundJobSeedSynchronizer</c> tüketicisi). Pasif (Suspended/Closed) kiracılar
/// döndürülmez - <see cref="Tenant.IsWritable"/> ile aynı kural.</summary>
public sealed class TenantDirectory(IdentityDbContext db) : IActiveTenantDirectory
{
    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken ct) =>
        await db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantStatus.Trial || t.Status == TenantStatus.Active)
            .Select(t => t.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
}
