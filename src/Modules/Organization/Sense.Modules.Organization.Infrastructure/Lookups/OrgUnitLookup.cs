using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Implements Organization.Contracts.IOrgUnitLookup so other modules (e.g. Employee) can validate an OrgUnitId reference.</summary>
public sealed class OrgUnitLookup(OrganizationDbContext context) : IOrgUnitLookup
{
    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid orgUnitId, CancellationToken cancellationToken = default) =>
        await context.OrgUnits.AnyAsync(u => u.TenantId == tenantId && u.Id == orgUnitId && u.IsActive, cancellationToken);
}
