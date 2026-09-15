using Sense.Shared.Kernel;
using Sense.Shared.Kernel.Domain;
using Sense.Shared.Kernel.Results;

namespace Sense.Modules.Organization.Domain.ApprovalPolicies;

/// <summary>
/// Which approval policy applies to a request type at an org unit (11.3.02 "Birime onay süreci ataması" / "Opsiyonel
/// alanlar"); one row per (tenant, unit, request type). No row = the unit inherits from its ancestors (see
/// <see cref="ApprovalPolicyInheritance"/>). ApprovalPolicyId is owned by the Workflow module, so it has no foreign key.
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

    /// <summary>ASCII PascalCase identifier ("Leave", "VisaDocument"...), see <see cref="IsValidRequestType"/>; a string so Workflow can add types without a migration.</summary>
    public string RequestType { get; private set; } = string.Empty;

    /// <summary>Cross-module reference to Workflow's ApprovalPolicy; no FK.</summary>
    public Guid ApprovalPolicyId { get; private set; }

    /// <summary>Uppercase ASCII letter followed by ASCII letters/digits, at most <see cref="OrganizationLimits.RequestTypeMaxLength"/> characters.</summary>
    public static bool IsValidRequestType(string? requestType) =>
        !string.IsNullOrEmpty(requestType)
        && requestType.Length <= OrganizationLimits.RequestTypeMaxLength
        && char.IsAsciiLetterUpper(requestType[0])
        && requestType.All(char.IsAsciiLetterOrDigit);

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

        if (!IsValidRequestType(requestType))
        {
            return Error.Validation(OrganizationDomainErrors.ApprovalAssignmentRequestTypeInvalid);
        }

        if (approvalPolicyId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.ApprovalAssignmentPolicyRequired);
        }

        var assignment = new OrgUnitApprovalPolicyAssignment(
            id ?? Guid.CreateVersion7(),
            tenantId,
            orgUnitId,
            Guard.MaxLength(requestType, OrganizationLimits.RequestTypeMaxLength),
            approvalPolicyId);

        assignment.Raise(new OrgUnitApprovalPolicyAssigned(assignment.Id, tenantId, orgUnitId, assignment.RequestType, approvalPolicyId));
        return assignment;
    }

    /// <summary>Points the assignment at another policy; returns false (no event) when it already uses that policy.</summary>
    public Result<bool> Reassign(Guid approvalPolicyId)
    {
        if (approvalPolicyId == Guid.Empty)
        {
            return Error.Validation(OrganizationDomainErrors.ApprovalAssignmentPolicyRequired);
        }

        if (approvalPolicyId == ApprovalPolicyId)
        {
            return false;
        }

        ApprovalPolicyId = approvalPolicyId;
        Raise(new OrgUnitApprovalPolicyAssigned(Id, TenantId, OrgUnitId, RequestType, approvalPolicyId));
        return true;
    }

    /// <summary>Marks the assignment as cleared; the caller removes the row so the unit inherits again.</summary>
    public void Clear() => Raise(new OrgUnitApprovalPolicyCleared(Id, TenantId, OrgUnitId, RequestType));
}

public sealed record OrgUnitApprovalPolicyAssigned(Guid AssignmentId, Guid TenantId, Guid OrgUnitId, string RequestType, Guid ApprovalPolicyId) : DomainEvent;

public sealed record OrgUnitApprovalPolicyCleared(Guid AssignmentId, Guid TenantId, Guid OrgUnitId, string RequestType) : DomainEvent;
