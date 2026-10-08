using Microsoft.EntityFrameworkCore;
using Sense.Modules.Identity.Domain.Tenants;
using Sense.Modules.Identity.Infrastructure.Persistence;
using Sense.Shared.Contracts.Modules;

namespace Sense.Modules.Identity.Infrastructure.Security;

public sealed class CommercialEntitlements(IdentityDbContext db) : ICommercialEntitlements
{
    public async Task<int?> GetEmployeeLimitAsync(Guid tenantId, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct).ConfigureAwait(false);
        return tenant is null ? null : CommercialLicense.Read(tenant.SettingsJson)?.EmployeeLimit;
    }
}
