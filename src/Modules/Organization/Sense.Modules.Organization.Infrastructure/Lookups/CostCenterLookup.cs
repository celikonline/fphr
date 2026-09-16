using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Implements Organization.Contracts.ICostCenterLookup so other modules can resolve a CostCenterId reference.</summary>
public sealed class CostCenterLookup(OrganizationDbContext context) : ICostCenterLookup
{
    public async Task<CostCenterSummary?> FindAsync(Guid tenantId, Guid costCenterId, CancellationToken cancellationToken = default)
    {
        var costCenter = await context.CostCenters
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == costCenterId, cancellationToken);

        return costCenter is null ? null : new CostCenterSummary(costCenter.Id, costCenter.Code, costCenter.Name, costCenter.IsActive);
    }
}
