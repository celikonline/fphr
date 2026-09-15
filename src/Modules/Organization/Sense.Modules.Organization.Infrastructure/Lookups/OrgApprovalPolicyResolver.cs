using Microsoft.EntityFrameworkCore;
using Sense.Modules.Organization.Contracts;
using Sense.Modules.Organization.Domain.ApprovalPolicies;
using Sense.Modules.Organization.Infrastructure.Persistence;

namespace Sense.Modules.Organization.Infrastructure.Lookups;

/// <summary>Implements Organization.Contracts.IOrgApprovalPolicyResolver on top of <see cref="ApprovalPolicyInheritance"/>.</summary>
public sealed class OrgApprovalPolicyResolver(OrganizationDbContext context) : IOrgApprovalPolicyResolver
{
    public async Task<ApprovalPolicyResolution?> ResolveAsync(Guid tenantId, Guid orgUnitId, string requestType, CancellationToken ct = default)
    {
        if (!OrgUnitApprovalPolicyAssignment.IsValidRequestType(requestType))
        {
            return null;
        }

        var nodes = await context.OrgUnits.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .Select(u => new OrgUnitNode(u.Id, u.ParentId, u.IsActive))
            .ToDictionaryAsync(n => n.Id, ct);

        var path = ApprovalPolicyInheritance.ActivePath(orgUnitId, nodes);
        if (path.Count == 0)
        {
            return null;
        }

        var pathIds = path.ToArray();
        var assignments = await context.ApprovalPolicyAssignments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.RequestType == requestType && pathIds.Contains(a.OrgUnitId))
            .ToListAsync(ct);

        var effective = ApprovalPolicyInheritance.Resolve(path, assignments, requestType);
        return effective is null
            ? null
            : new ApprovalPolicyResolution(effective.ApprovalPolicyId, effective.SourceOrgUnitId, effective.SourceOrgUnitId != orgUnitId);
    }
}
