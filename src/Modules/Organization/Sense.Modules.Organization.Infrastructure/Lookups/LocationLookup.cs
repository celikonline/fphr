using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Implements Organization.Contracts.ILocationLookup so other modules can validate a LocationId reference.</summary>
public sealed class LocationLookup(OrganizationDbContext context) : ILocationLookup
{
    public async Task<bool> ExistsActiveAsync(Guid tenantId, Guid locationId, CancellationToken cancellationToken = default) =>
        await context.Locations.AnyAsync(l => l.TenantId == tenantId && l.Id == locationId && l.IsActive, cancellationToken);
}
