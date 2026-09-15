using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.ApprovalPolicies;

/// <summary>
/// Forward-looking stub (roadmap item #4 / 11.3.02-sirket-yapisi.md "opsiyonel alanlar"): records
/// which approval policy applies to a given request type at a given org unit. The Workflow module
/// (not built yet - a later roadmap item) owns ApprovalPolicyId and the actual resolution algorithm
/// (walk the unit's ancestor chain, first match wins, else Workflow's own default); Organization only
/// stores the assignment slot, with no FK to ApprovalPolicyId since that table does not exist yet.
/// Kept intentionally minimal: no commands/queries/API wired up in this item, just the persisted shape
/// so Workflow can read/write it later via Organization's Contracts once it exists.
/// </summary>
public sealed class OrgUnitApprovalPolicyAssignment : TenantAggregateRoot<Guid>, IAuditLogged
{
    private OrgUnitApprovalPolicyAssignment()
    {
    }

    private OrgUnitApprovalPolicyAssignment(Guid id, Guid tenantId, Guid orgUnitId, string requestType, Guid approvalPolicyId)
        : base(id, tenantId)
    {
        OrgUnitId = orgUnitId;
        RequestType = requestType;
        ApprovalPolicyId = approvalPolicyId;
    }

    public Guid OrgUnitId { get; private set; }

    /// <summary>"Leave" | "Expense" | "Overtime" | ... - a plain string (not an enum) so Workflow can introduce new request types without an Organization migration.</summary>
    public string RequestType { get; private set; } = string.Empty;

    /// <summary>Cross-module reference to a future Workflow.ApprovalPolicy; no FK (Workflow module does not exist yet).</summary>
    public Guid ApprovalPolicyId { get; private set; }

    public static Result<OrgUnitApprovalPolicyAssignment> Create(Guid tenantId, Guid orgUnitId, string requestType, Guid approvalPolicyId, Guid? id = null)
    {
        if (orgUnitId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.ApprovalAssignmentOrgUnitRequired);
        }

        if (string.IsNullOrWhiteSpace(requestType))
        {
            return Error.Validation(OrganizationDomainErrors.ApprovalAssignmentRequestTypeRequired);
        }

        if (approvalPolicyId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.ApprovalAssignmentPolicyRequired);
        }

        var assignment = new OrgUnitApprovalPolicyAssignment(
            id ?? Guid.CreateVersion7(),
            tenantId,
            orgUnitId,
            Guard.MaxLength(requestType.Trim(), OrganizationLimits.RequestTypeMaxLength),
            approvalPolicyId);

        assignment.Raise(new OrgUnitApprovalPolicyAssigned(assignment.Id, tenantId, orgUnitId, assignment.RequestType, approvalPolicyId));
        return assignment;
    }

    public void Reassign(Guid approvalPolicyId)
    {
        ApprovalPolicyId = approvalPolicyId;
        Raise(new OrgUnitApprovalPolicyAssigned(Id, TenantId, OrgUnitId, RequestType, approvalPolicyId));
    }
}

public sealed record OrgUnitApprovalPolicyAssigned(Guid AssignmentId, Guid TenantId, Guid OrgUnitId, string RequestType, Guid ApprovalPolicyId) : DomainEvent;
